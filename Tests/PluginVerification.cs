using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ShiroBot.Model.QQ;
using ShiroBot.Plugin.VideoTool;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

internal static class PluginVerification
{
    internal static async Task RunAsync(string root, string input, string audioInput, string ffmpeg, string gifsicle, CancellationToken token)
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var server = new HttpListener();
        var baseUrl = $"http://127.0.0.1:{port}/";
        server.Prefixes.Add(baseUrl);
        server.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = ServeAsync(server, input, audioInput, slowStarted, stop.Token);
        var replies = new ConcurrentQueue<MessageSegment[]>();
        var notices = new List<string>();
        var deleted = new List<MessageReference>();
        var reactions = new List<(string Face, bool Add)>();
        var failNextOutput = false;
        var rejectReactions = false;
        IQGroupApi? groupApi = null;
        var reactionApi = StubProxy.Make<IQGroupApi>((method, args) =>
        {
            if (method.Name != "SendMessageReactionAsync") throw new NotSupportedException(method.Name);
            if (rejectReactions) throw new NotSupportedException("Reactions unsupported");
            reactions.Add(((string)args![2]!, (bool)args[3]!));
            return Task.CompletedTask;
        });
        var quotedReplies = 0;
        var tempDirectories = new List<string>();
        var config = new PluginConfig { FfmpegPath = ffmpeg, GifsiclePath = gifsicle, Width = 64, Height = 64, DurationSeconds = 0.5, MaxInputMiB = 1 };
        var quoted = Message(Channel.Group("group"), new VideoSegment("") { ResourceId = "resource" });
        var messages = StubProxy.Make<IMessageContext>((method, args) => method.Name switch
        {
            "ReplyAsync" or "QuoteReplyAsync" => Reply(args!, method.Name == "QuoteReplyAsync"),
            "GetMessageAsync" => Task.FromResult<MessageEvent?>(quoted),
            "GetResourceUrlAsync" => Task.FromResult(baseUrl + "source.mp4"),
            "DeleteMessageAsync" => Delete(args!),
            _ => throw new NotSupportedException(method.Name)
        });
        var settings = StubProxy.Make<IConfigContext>((method, _) => method.Name == "Load" ? config : throw new NotSupportedException(method.Name));
        var host = StubProxy.Make<IBotContext>((method, args) => method.Name switch
        {
            "get_Config" => settings,
            "get_Message" => messages,
            "CreateTempDirectory" => Temp((TimeSpan)args![0]!),
            "GetAdapterExtension" => groupApi,
            _ => throw new NotSupportedException(method.Name)
        });
        var plugin = new VideoToolPlugin();
        await plugin.OnLoad(host);
        var unloaded = false;
        try
        {
            var events = (IBotEventSubscriber)plugin;
            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("#gif " + baseUrl + "source.mp4")));
            Check(replies.SelectMany(x => x).OfType<ImageSegment>().Count() == 1, "Direct command downloads, converts and replies with an image");
            Check(notices.Count == 1 && deleted.Single().MessageId == notices.Single() && deleted[0].InstanceId == "test-instance", "Without reactions, the processing text is recalled from the original instance after completion");
            var initialImage = replies.SelectMany(x => x).OfType<ImageSegment>().Single();
            Check(File.Exists(new Uri(initialImage.Uri).LocalPath) && tempDirectories.All(d => !File.Exists(Path.Combine(d, "input.mp4"))), "Output survives sending while downloaded input is removed");
            await ((IConfigurableComponent)plugin).ApplyConfigAsync(new PluginConfig { FfmpegPath = ffmpeg, GifsiclePath = gifsicle, Width = 80, Height = 48, DurationSeconds = 0.5, MaxInputMiB = 1 });
            groupApi = reactionApi;
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("2gif")));
            Check(reactions.SequenceEqual(new[] { ("351", true), ("351", false), ("426", true) }) && notices.Count == 1, "Processing reaction is replaced with MyParser's completion reaction after successful sending");
            var images = replies.SelectMany(x => x).OfType<ImageSegment>().ToArray();
            Check(images.Length == 2 && images[1].Width == 80 && images[1].Height == 48 && quotedReplies == 2, "2gif on a quoted group video converts and quotes the command using hot-updated config");

            quoted = Message(Channel.Direct("user"), new FileSegment(baseUrl + "source.mp4") { FileName = "clip.MKV", FileSize = new FileInfo(input).Length });
            await events.OnEventAsync(Message(Channel.Direct("user"), new QuoteSegment("source"), new TextSegment("2gif -25")));
            Check(replies.SelectMany(x => x).OfType<ImageSegment>().Count() == 3, "2gif with crop offsets routes and converts a quoted video file in direct chat");

            var directoriesBefore = tempDirectories.Count;
            quoted = Message(Channel.Group("group"), new FileSegment(baseUrl + "source.mp4") { FileName = "large.mp4", FileSize = 1024L * 1024L });
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("2gif")));
            Check(tempDirectories.Count == directoriesBefore && replies.SelectMany(x => x).OfType<TextSegment>().Any(t => t.Text.Contains("视频须小于 1 MiB")), "File metadata at the configured size boundary rejects before downloading");

            quoted = Message(Channel.Group("group"), new FileSegment(baseUrl + "source.mp4") { FileName = "notes.txt" });
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("2gif")));
            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("2gif"), new VideoSegment(baseUrl + "source.mp4")));
            Check(tempDirectories.Count == directoriesBefore, "2gif requires a quote and ignores non-video files");
            try
            {
                await ((IConfigurableComponent)plugin).ApplyConfigAsync(new PluginConfig { Width = -1 });
                throw new Exception("Invalid settings were accepted");
            }
            catch (ArgumentException) { }
            Check(((IConfigurableComponent<PluginConfig>)plugin).CurrentConfigValue.Width == 80, "Invalid hot update preserves the previous settings");

            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("#gif " + baseUrl + "oversize.mp4")));
            Check(replies.SelectMany(x => x).OfType<TextSegment>().Any(t => t.Text.Contains("下载限制")), "Oversize HTTP response is rejected before download");
            Check(notices.All(id => deleted.Any(d => d.MessageId == id)), "Processing text is also recalled after a failed download");
            var imagesBefore = replies.SelectMany(x => x).OfType<ImageSegment>().Count();
            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("#gif " + baseUrl + "chunked.mp4")));
            Check(replies.SelectMany(x => x).OfType<ImageSegment>().Count() == imagesBefore && tempDirectories.All(d => !File.Exists(Path.Combine(d, "input.mp4"))), "Download without Content-Length stops at the actual byte limit and removes the partial input");
            var count = replies.Count;
            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("#gift")));
            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("2gifextra")));
            Check(replies.Count == count, "Other command prefixes do not trigger conversion");

            await ((IConfigurableComponent)plugin).ApplyConfigAsync(new PluginConfig { FfmpegPath = ffmpeg, GifsiclePath = gifsicle, VideoMaxHeight = 180, VideoMaxFps = 12, VideoPreset = "veryfast", AllowCpuFallback = true });
            quoted = Message(Channel.Group("group"), new VideoSegment(baseUrl + "audio.mp4"));
            rejectReactions = true;
            var noticesBefore = notices.Count;
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("vinfo")));
            Check(replies.SelectMany(x => x).OfType<TextSegment>().Any(t => t.Text.Contains("640×360") && t.Text.Contains("aac")), "Quoted vinfo replies with video and audio metadata");
            Check(notices.Count == noticesBefore + 1 && deleted.Any(d => d.MessageId == notices[^1]), "An adapter rejecting reactions falls back to text and recalls it after completion");
            rejectReactions = false;
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("vclip 0.5 1.5")));
            Check(replies.SelectMany(x => x).OfType<VideoSegment>().Any(v => v.FileName == "clip.mp4"), "vclip replies with a video segment");
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("2mp3")));
            Check(replies.Any(x => x is [FileSegment { FileName: "audio.mp3" }]), "2mp3 sends one file segment, compatible with Milky file uploads");
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("vcover 1")));
            Check(replies.SelectMany(x => x).OfType<ImageSegment>().Any(v => v.FileName == "cover.jpg"), "vcover replies with a JPEG image");
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("vcompress 0.12")));
            Check(replies.SelectMany(x => x).OfType<VideoSegment>().Any(v => v.FileName == "compressed.mp4"), "vcompress replies with the compressed video");
            failNextOutput = true;
            await events.OnEventAsync(Message(Channel.Group("group"), new QuoteSegment("source"), new TextSegment("vinfo")));
            Check(reactions.TakeLast(3).SequenceEqual(new[] { ("351", true), ("351", false), ("9", true) }), "Failed output sending produces the failure reaction instead of the completion reaction");
            var beforeHelp = tempDirectories.Count;
            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("vhelp")));
            Check(beforeHelp == tempDirectories.Count && replies.SelectMany(x => x).OfType<TextSegment>().Any(t => t.Text.Contains("vclip 00:05 00:15")), "Help works without a quote or download");

            await events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("vhw")));
            Check(replies.SelectMany(x => x).OfType<TextSegment>().Any(t => t.Text.Contains("H.264 编码器实际探测") && t.Text.Contains("h264_v4l2m2m")), "vhw reports real backend availability without a quoted video");
            var action = await ((IPluginActionProvider)plugin).ExecuteActionAsync("probe-encoders", token);
            Check(action.Ok && action.Message.Contains("libx264") && ((IPluginActionProvider)plugin).Actions.Any(a => a.Id == "probe-encoders"), "Dashboard action refreshes probe results and exposes details");

            var running = events.OnEventAsync(Message(Channel.Direct("user"), new TextSegment("#gif " + baseUrl + "slow.mp4")));
            await slowStarted.Task.WaitAsync(token);
            await events.OnEventAsync(Message(Channel.Group("group"), new TextSegment("#gif " + baseUrl + "source.mp4")));
            Check(replies.SelectMany(x => x).OfType<TextSegment>().Any(t => t.Text.Contains("稍后重试")), "Concurrent command gets a busy reply");
            await plugin.OnUnload().WaitAsync(TimeSpan.FromSeconds(5), token);
            unloaded = true;
            await running;
            Check(tempDirectories.All(d => !File.Exists(Path.Combine(d, "input.mp4"))), "Unload cancels an active download and waits for cleanup");
            Check(notices.All(id => deleted.Any(d => d.MessageId == id)), "Plugin unload also recalls the active processing notice");
        }
        finally
        {
            if (!unloaded) await plugin.OnUnload();
            await stop.CancelAsync();
            server.Stop();
            try { await serving; }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) { }
        }

        object Reply(object?[] args, bool quote)
        {
            if (quote) quotedReplies++;
            var segments = args[1] is string text ? [new TextSegment(text)] : ((IEnumerable<MessageSegment>)args[1]!).ToArray();
            if (quote && segments.OfType<FileSegment>().Any()) throw new Exception("Milky cannot mix quotes and file uploads.");
            if (quote && segments.Any(s => s is VideoSegment || s is ImageSegment image && !string.Equals(Path.GetExtension(image.FileName), ".gif", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Only GIF media can be sent with a quote.");
            replies.Enqueue(segments);
            if (failNextOutput)
            {
                failNextOutput = false;
                return Task.FromResult(new SentMessage("") { IsSuccess = false });
            }
            var id = Guid.NewGuid().ToString("N");
            if (segments.OfType<TextSegment>().Any(t => t.Text == "正在处理视频…")) notices.Add(id);
            return Task.FromResult(new SentMessage(id));
        }

        object Delete(object?[] args)
        {
            deleted.Add((MessageReference)args[0]!);
            return Task.CompletedTask;
        }

        TemporaryDirectory Temp(TimeSpan retention)
        {
            var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            tempDirectories.Add(directory);
            return new(directory, DateTimeOffset.UtcNow + retention);
        }
    }

    private static MessageEvent Message(Channel channel, params MessageSegment[] segments) => new()
    {
        Platform = "test", InstanceId = "test-instance", MessageId = "message", Channel = channel,
        Sender = new User("user"), Segments = segments
    };

    private static async Task ServeAsync(HttpListener listener, string input, string audioInput, TaskCompletionSource slowStarted, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var request = await listener.GetContextAsync().WaitAsync(token);
            try
            {
                if (request.Request.Url!.AbsolutePath == "/slow.mp4")
                {
                    slowStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                else if (request.Request.Url.AbsolutePath == "/oversize.mp4")
                {
                    request.Response.ContentLength64 = 200 * 1024 * 1024;
                    await request.Response.OutputStream.FlushAsync(token);
                }
                else if (request.Request.Url.AbsolutePath == "/chunked.mp4")
                {
                    request.Response.SendChunked = true;
                    var buffer = new byte[64 * 1024];
                    for (var index = 0; index < 16; index++)
                        await request.Response.OutputStream.WriteAsync(buffer, token);
                }
                else
                {
                    await using var source = File.OpenRead(request.Request.Url.AbsolutePath == "/audio.mp4" ? audioInput : input);
                    request.Response.ContentLength64 = source.Length;
                    await source.CopyToAsync(request.Response.OutputStream, token);
                }
            }
            finally { request.Response.Close(); }
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
}

public class StubProxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> _call = null!;
    internal static T Make<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var result = Create<T, StubProxy>();
        ((StubProxy)(object)result)._call = call;
        return result;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _call(targetMethod!, args);
}
