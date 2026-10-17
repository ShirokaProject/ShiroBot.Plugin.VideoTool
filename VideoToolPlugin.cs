using ShiroBot.Model.QQ;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "1.0.0")]

namespace ShiroBot.Plugin.VideoTool;

[BotPlugin("VideoToGif", Name = "VideoTool 视频工具箱", Version = "0.1.1", Author = "greepar",
    Description = "视频 GIF、截取、MP3、目标大小压缩、封面和信息查询；支持跨平台硬件编码实测、CPU 回退及 WebUI 配置。",
    GithubRepo = "ShirokaProject/ShiroBot.Plugin.VideoTool",
    Category = PluginCategory.Utility, IsPluginSingleFile = true,
    SharedAssemblies = "ShiroBot.Model.QQ;ShiroBot.Model.Discord;ShiroBot.Model.Telegram")]
public sealed class VideoToolPlugin : PluginBase<PluginConfig>, IPluginActionProvider
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _unload = new();
    private readonly object _gate = new();
    private TaskCompletionSource? _active;
    private bool _stopping;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".webm", ".flv", ".wmv", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".3gp", ".3g2", ".ogv"
    };

    protected override void ConfigureRoutes()
    {
        foreach (var router in new[] { GroupCommands, DirectCommands })
        {
            foreach (var command in new[] { "2mp3", "vinfo", "vhelp", "vhw" }) router.MapExact(command, HandleAsync);
            foreach (var command in new[] { "2gif", "#gif", "vclip", "vcompress", "vcover" }) router.MapPrefix(command, HandleAsync);
        }
    }

    protected override Task OnConfigLoadedAsync(PluginConfig config, CancellationToken cancellationToken)
    {
        config.Validate();
        return Task.CompletedTask;
    }

    protected override Task OnConfigChangedAsync(PluginConfig previous, PluginConfig current, CancellationToken cancellationToken)
    {
        current.Validate();
        return Task.CompletedTask;
    }

    private async Task HandleAsync(MessageEvent message)
    {
        var text = message.GetPlainText().Trim();
        VideoCommand? command;
        try { command = VideoCommand.Parse(text); }
        catch (ArgumentException error) { await ReportAsync(message, error.Message); return; }
        if (command is null) return;
        if (command.Operation == ToolOperation.Help) { await ReportAsync(message, VideoCommand.Help); return; }
        bool busy;
        lock (_gate)
        {
            if (_stopping) return;
            busy = _active is not null;
            if (!busy) _active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (busy)
        {
            await Context.Message.ReplyAsync(message, "正在处理另一个视频，请稍后重试。");
            return;
        }

        // Keep one immutable-by-convention settings instance for the entire task.
        var config = Settings;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_unload.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var token = timeout.Token;
        string? input = null;
        try
        {
            config.Validate();
            if (command.Operation == ToolOperation.Hardware)
            {
                var probeDirectory = Context.CreateTempDirectory(TimeSpan.FromSeconds(config.TimeoutSeconds + 60));
                var report = await HardwareEncoders.ProbeAsync(config, probeDirectory.Path, token);
                await Context.Message.ReplyAsync(message, [new TextSegment(report.Describe())], token);
                return;
            }
            var url = await ResolveSourceAsync(message, command.Url ?? "", command.QuoteOnly, config.MaxInputMiB * 1024L * 1024L, token);
            if (url is null)
            {
                await Context.Message.ReplyAsync(message, [new TextSegment("请引用一条视频消息或带视频后缀的文件消息再发送命令。发送 vhelp 查看用法。")], token);
                return;
            }
            // Report missing tools before downloading a potentially large video.
            VideoOperations.CheckTools(command.Operation, config);
            var temp = Context.CreateTempDirectory(TimeSpan.FromSeconds(config.TimeoutSeconds + 600));
            input = Path.Combine(temp.Path, "input.mp4");
            await Context.Message.ReplyAsync(message, [new TextSegment("正在处理视频…")], token);
            await DownloadAsync(url, input, config.MaxInputMiB * 1024L * 1024L, token);
            var output = await VideoOperations.ExecuteAsync(command, input, temp.Path, config, token);
            var sent = await SendOutputAsync(message, output, token);
            if (!sent.IsSuccess)
                await Context.Message.ReplyAsync(message, [new TextSegment("处理已完成，但平台发送失败。请检查适配器的媒体支持、文件权限或降低输出大小。")], token);
            // The host keeps output alive for adapters that upload local files asynchronously.
        }
        catch (OperationCanceledException) when (_unload.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            await ReportAsync(message, $"任务超过 {config.TimeoutSeconds} 秒，已停止。可以在配置中缩短截取时长或增加超时。");
        }
        catch (Exception error)
        {
            BotLog.Error($"VideoTool 处理失败：{error}");
            var detail = error.Message.Length > 400 ? error.Message[..400] + "…" : error.Message;
            await ReportAsync(message, $"处理失败：{detail}");
        }
        finally
        {
            if (input is not null)
            {
                try { File.Delete(input); }
                catch (IOException) { /* Host cleanup will retry. */ }
                catch (UnauthorizedAccessException) { }
            }
            lock (_gate)
            {
                var finished = _active;
                _active = null;
                finished?.TrySetResult();
            }
        }
    }

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [new("probe-encoders", "重新探测硬件编码器", "实际编码 12 帧并验证输出，刷新探测缓存。", Tone: "primary")];

    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        if (actionId != "probe-encoders") return new(false, "未知操作。");
        lock (_gate)
        {
            if (_stopping || _active is not null) return new(false, "正在处理任务，请稍后重试。");
            _active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var config = Settings;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _unload.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        try
        {
            config.Validate();
            var temp = Context.CreateTempDirectory(TimeSpan.FromSeconds(config.TimeoutSeconds + 60));
            var report = await HardwareEncoders.ProbeAsync(config, temp.Path, timeout.Token, refresh: true);
            return new(true, report.Describe());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "探测超时或插件正在卸载。"); }
        catch (Exception error) when (error is not OperationCanceledException) { return new(false, error.Message); }
        finally
        {
            lock (_gate)
            {
                var finished = _active;
                _active = null;
                finished?.TrySetResult();
            }
        }
    }

    private Task<SentMessage> SendOutputAsync(MessageEvent message, ToolOutput output, CancellationToken token)
    {
        if (output.Kind == OutputKind.Text)
            return Context.Message.QuoteReplyAsync(message, [new TextSegment(output.Text!)], token);
        var path = output.Path!;
        var uri = new Uri(path).AbsoluteUri;
        if (output.Kind == OutputKind.File)
            return Context.Message.ReplyAsync(message,
                [new FileSegment(uri) { FileName = Path.GetFileName(path), FileSize = new FileInfo(path).Length }], token);
        MessageSegment segment = output.Kind == OutputKind.Video
            ? new VideoSegment(uri) { FileName = Path.GetFileName(path) }
            : new ImageSegment(uri) { FileName = Path.GetFileName(path), Width = output.Width, Height = output.Height };
        return Context.Message.QuoteReplyAsync(message, [segment], token);
    }

    private async Task ReportAsync(MessageEvent message, string text)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_unload.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await Context.Message.ReplyAsync(message, [new TextSegment(text)], timeout.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { BotLog.Warning($"无法发送 VideoTool 提示：{error.Message}"); }
    }

    private async Task<string?> ResolveSourceAsync(MessageEvent message, string argument, bool quoteOnly, long limit, CancellationToken token)
    {
        if (argument.Length > 0) return RequireHttpUrl(argument);
        var source = message;
        if (quoteOnly || !source.Segments.Any(IsVideo))
        {
            if (source.GetQuote() is not { } quote) return null;
            var quoted = await Context.Message.GetMessageAsync(message.Channel, quote.MessageId, token);
            if (quoted is null) return null;
            source = quoted;
        }
        var segment = source.Segments.OfType<ResourceSegment>().FirstOrDefault(IsVideo);
        if (segment is null) return null;
        if (segment is FileSegment file)
        {
            var size = file.FileSize ?? (source.Raw as QIncomingMessage)?.Segments.OfType<QIncomingFile>()
                .FirstOrDefault(raw => raw.FileId == file.ResourceId)?.FileSize;
            if (size >= limit) throw new InvalidDataException($"视频须小于 {limit / 1024 / 1024} MiB 下载限制。");
        }
        if (Uri.TryCreate(segment.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.AbsoluteUri;

        if (!string.IsNullOrWhiteSpace(segment.ResourceId))
        {
            if (segment is FileSegment && Context.GetAdapterExtension<IQFileApi>() is { } fileApi)
            {
                if (source.Channel.Type == ChannelType.Group)
                    return RequireHttpUrl(await fileApi.GetGroupFileDownloadUrlAsync(source.Channel.Id, segment.ResourceId, token));
                var raw = (source.Raw as QIncomingMessage)?.Segments.OfType<QIncomingFile>()
                    .FirstOrDefault(file => file.FileId == segment.ResourceId);
                if (!string.IsNullOrWhiteSpace(raw?.FileHash))
                    return RequireHttpUrl(await fileApi.GetPrivateFileDownloadUrlAsync(source.Sender.Id, segment.ResourceId, raw.FileHash, token));
            }
            return RequireHttpUrl(await Context.Message.GetResourceUrlAsync(segment.ResourceId, token));
        }
        throw new InvalidOperationException("适配器没有提供可下载的视频地址，请检查适配器的资源解析支持。");
    }

    internal static bool IsVideo(MessageSegment segment) => segment is VideoSegment ||
        segment is FileSegment file && VideoExtensions.Contains(Path.GetExtension(file.FileName) ?? "");

    private static string RequireHttpUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("请输入 HTTP(S) 视频直链。");
        return uri.AbsoluteUri;
    }

    private async Task DownloadAsync(string url, string path, long limit, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength >= limit)
            throw new InvalidDataException($"视频须小于 {limit / 1024 / 1024} MiB 下载限制。");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(path);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total >= limit) throw new InvalidDataException($"视频须小于 {limit / 1024 / 1024} MiB 下载限制。");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        if (total == 0) throw new InvalidDataException("下载的视频为空。");
    }

    protected override async Task OnUnloadAsync()
    {
        Task active;
        lock (_gate)
        {
            _stopping = true;
            active = _active?.Task ?? Task.CompletedTask;
        }
        await _unload.CancelAsync();
        await active;
        _http.Dispose();
        _unload.Dispose();
    }
}
