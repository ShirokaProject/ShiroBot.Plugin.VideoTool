using System.Globalization;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Plugin.VideoTool;

internal enum OutputKind { Image, Video, File, Text }
internal sealed record ToolOutput(OutputKind Kind, string? Path = null, string? Text = null, int? Width = null, int? Height = null);

internal static class VideoOperations
{
    internal static void CheckTools(ToolOperation operation, PluginConfig config)
    {
        ExecutableLocator.Resolve(config.FfmpegPath, "ffmpeg");
        if (operation == ToolOperation.Gif) ExecutableLocator.Resolve(config.GifsiclePath, "gifsicle");
        else ExecutableLocator.ResolveProbe(config);
    }

    internal static async Task<ToolOutput> ExecuteAsync(VideoCommand command, string input, string directory, PluginConfig config, CancellationToken token)
    {
        if (command.Operation == ToolOperation.Compress) config = config.ForCompressionCommand(command);
        config.Validate();
        if (command.Operation == ToolOperation.Gif)
        {
            var gifConfig = config.ForGifCommand(command);
            return new(OutputKind.Image, await GifConverter.ConvertAsync(input, directory, gifConfig, token), Width: gifConfig.Width, Height: gifConfig.Height);
        }
        var info = await MediaProbe.ReadAsync(input, config, token);
        if (command.Operation == ToolOperation.Info) return new(OutputKind.Text, Text: info.Describe());
        var path = Path.Combine(directory, command.Operation switch
        {
            ToolOperation.Clip => "clip.mp4", ToolOperation.Mp3 => "audio.mp3",
            ToolOperation.Compress => "compressed.mp4", ToolOperation.Cover => "cover.jpg",
            _ => throw new ArgumentException("不支持的操作。")
        });
        var complete = false;
        string? encodingNote = null;
        try
        {
            switch (command.Operation)
            {
                case ToolOperation.Clip:
                    if (command.Start >= info.Duration || command.End > info.Duration + 0.05)
                        throw new ArgumentException($"截取时间超出视频时长（{N(info.Duration)} 秒）。");
                    if (command.End - command.Start > config.MaxClipSeconds)
                        throw new ArgumentException($"一次最多截取 {config.MaxClipSeconds} 秒，可在配置中调整。");
                    var clip = Input(input, command.Start);
                    clip.AddRange(["-t", N(command.End - command.Start), "-map", "0:v:0", "-map", "0:a:0?", "-vf", VideoFilter(info, config)]);
                    clip.AddRange(VideoEncoding(config));
                    clip.AddRange(["-crf", N(config.VideoCrf), "-c:a", "aac", "-b:a", $"{config.VideoAudioKbps}k", "-movflags", "+faststart", path]);
                    await RunAsync(config, clip, token);
                    break;
                case ToolOperation.Mp3:
                    if (info.AudioCodec is null) throw new InvalidDataException("视频没有音轨，无法提取 MP3。");
                    var audio = Input(input);
                    audio.AddRange(["-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-b:a", $"{config.Mp3BitrateKbps}k", path]);
                    await RunAsync(config, audio, token);
                    break;
                case ToolOperation.Cover:
                    if (command.Start >= info.Duration) throw new ArgumentException($"截图时间须小于视频时长（{N(info.Duration)} 秒）。");
                    var cover = Input(input, command.Start);
                    cover.AddRange(["-map", "0:v:0", "-frames:v", "1", "-q:v", N(config.CoverQuality), "-update", "1", path]);
                    await RunAsync(config, cover, token);
                    break;
                case ToolOperation.Compress:
                    var target = command.TargetMiB ?? config.CompressionTargetMiB;
                    if (!double.IsFinite(target) || target < 0.1 || target > config.MaxOutputMiB)
                        throw new ArgumentException($"压缩目标应在 0.1 到最大发送大小 {config.MaxOutputMiB} MiB 之间。");
                    var bytes = (long)(target * 1048576);
                    if (info.Size <= bytes) return new(OutputKind.Text, Text: $"视频已小于目标大小（{N(target)} MiB），无需压缩。");
                    encodingNote = await CompressAsync(input, path, directory, info, bytes, config, token);
                    break;
            }
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException("没有生成有效的输出文件。");
            if (new FileInfo(path).Length > config.MaxOutputMiB * 1048576L)
                throw new InvalidDataException($"输出超过 {config.MaxOutputMiB} MiB 发送限制，请缩短片段或降低质量参数。");
            complete = true;
            return new(command.Operation switch { ToolOperation.Mp3 => OutputKind.File, ToolOperation.Cover => OutputKind.Image, _ => OutputKind.Video }, path, Text: encodingNote);
        }
        finally { if (!complete) File.Delete(path); }
    }

    private static async Task<string> CompressAsync(string input, string output, string directory, MediaInfo info, long target, PluginConfig config, CancellationToken token)
    {
        if (config.CompressionEncoder != "cpu")
        {
            IReadOnlyList<EncoderProfile> profiles = [];
            var reason = "未找到通过实测的硬件编码器";
            try
            {
                var report = await HardwareEncoders.ProbeAsync(config, directory, token);
                profiles = HardwareEncoders.Select(report, config.CompressionEncoder);
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
            {
                token.ThrowIfCancellationRequested();
                reason = error.Message;
                BotLog.Warning($"VideoTool 硬件探测失败：{reason}");
            }
            foreach (var profile in profiles)
            {
                try
                {
                    await CompressHardwareAsync(input, output, info, target, config, profile, token);
                    BotLog.Info($"VideoTool 压缩使用 {profile.Name}{(profile.Device is null ? "" : " / " + profile.Device)}，输出 {new FileInfo(output).Length} 字节。");
                    return profile.Name;
                }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
                {
                    token.ThrowIfCancellationRequested();
                    File.Delete(output);
                    reason = error.Message;
                    BotLog.Warning($"VideoTool {profile.Name} 未完成本次压缩：{reason}");
                }
            }
            if (!config.AllowCpuFallback) throw new InvalidOperationException($"{reason}。CPU 编码回退已禁用，任务已停止。可增大目标体积、调整硬件接口或尝试另一种输出格式。");
            BotLog.Info("VideoTool 允许 CPU 回退，开始软件两遍编码。");
        }
        await CompressCpuAsync(input, output, directory, info, target, config, token);
        return config.CompressionCodec == "hevc" ? "libx265" : "libx264";
    }

    private static async Task CompressHardwareAsync(string input, string output, MediaInfo info, long target, PluginConfig config, EncoderProfile profile, CancellationToken token)
    {
        var budget = CompressionBudget.Create(info, target, config);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            BotLog.Info($"VideoTool {profile.Name} 尝试 {attempt + 1}/3：视频 {budget.VideoRate / 1000} kbps、音频 {budget.AudioRate / 1000} kbps，最高 {budget.Height}p/{budget.Fps} fps，目标 {target} 字节。");
            var args = Input(input);
            args.InsertRange(0, profile.InputArguments);
            args.AddRange(["-map", "0:v:0", "-vf", profile.Filter(VideoFilter(info, config, budget.Height, budget.Fps))]);
            args.AddRange(profile.EncodingArguments);
            args.AddRange(["-b:v", N(budget.VideoRate), "-maxrate", N(budget.VideoRate), "-bufsize", N(budget.VideoRate * 2d)]);
            if (budget.AudioRate > 0) args.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", N(budget.AudioRate)]);
            else args.Add("-an");
            if (config.CompressionCodec == "hevc") args.AddRange(["-tag:v", "hvc1"]);
            args.AddRange(["-movflags", "+faststart", output]);
            await RunAsync(config, args, token);
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new InvalidDataException("硬件编码没有输出有效视频。");
            var actual = new FileInfo(output).Length;
            if (actual <= target)
            {
                var encoded = await MediaProbe.ReadAsync(output, config, token);
                if (encoded.VideoCodec != config.CompressionCodec || Math.Abs(encoded.Duration - info.Duration) > Math.Max(0.5, info.Duration * 0.01) ||
                    (info.AudioCodec is not null && encoded.AudioCodec is null))
                    throw new InvalidDataException("硬件输出的视频编码、时长或音轨验证失败。");
                return;
            }
            BotLog.Info($"VideoTool {profile.Name} 输出 {actual} 字节，超过目标 {target}，降低分辨率/帧率及码率再试。");
            budget = budget.Retry(target, actual);
        }
        throw new InvalidDataException("硬件编码可用，但三次体积调整后仍超出目标大小，并非硬件探测失败");
    }

    private static async Task CompressCpuAsync(string input, string output, string directory, MediaInfo info, long target, PluginConfig config, CancellationToken token)
    {
        var budget = CompressionBudget.Create(info, target, config);
        var videoRate = budget.VideoRate;
        var audioRate = budget.AudioRate;
        var passLog = Path.Combine(directory, "video-pass");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (videoRate < 16000) throw new ArgumentException("目标大小过小，无法保留可用画质，请增大目标或先截取片段。");
                for (var pass = 1; pass <= 2; pass++)
                {
                    var args = Input(input);
                    args.AddRange(["-map", "0:v:0", "-vf", VideoFilter(info, config, budget.Height, budget.Fps)]);
                    args.AddRange(VideoEncoding(config, compression: true));
                    args.AddRange(["-b:v", N(videoRate), "-pass", N(pass), "-passlogfile", passLog]);
                    if (pass == 1) args.AddRange(["-an", "-f", "null", "-"]);
                    else
                    {
                        if (audioRate > 0) args.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", N(audioRate)]);
                        else args.Add("-an");
                        if (config.CompressionCodec == "hevc") args.AddRange(["-tag:v", "hvc1"]);
                        args.AddRange(["-movflags", "+faststart", output]);
                    }
                    await RunAsync(config, args, token);
                }
                var actual = new FileInfo(output).Length;
                if (actual <= target) return;
                videoRate = (int)(videoRate * (target / (double)actual) * 0.9);
            }
            throw new InvalidDataException("无法在当前时长和音频设置下达到目标大小，请增大目标或先截取片段。");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory, "video-pass*")) File.Delete(file);
        }
    }

    private static List<string> Input(string path, double start = 0) =>
        ["-y", "-nostdin", "-hide_banner", "-loglevel", "error", "-ss", N(start), "-protocol_whitelist", "file,pipe", "-i", path, "-map_metadata", "-1"];

    private static string[] VideoEncoding(PluginConfig config, bool compression = false) => compression && config.CompressionCodec == "hevc"
        ? ["-c:v", "libx265", "-preset", config.VideoPreset, "-pix_fmt", "yuv420p", "-x265-params", $"pools={config.FfmpegThreads}:frame-threads=1"]
        : ["-c:v", "libx264", "-preset", config.VideoPreset, "-pix_fmt", "yuv420p"];
    private static string VideoFilter(MediaInfo info, PluginConfig config, int? height = null, int? fps = null) =>
        FormattableString.Invariant($"scale=trunc(iw*min(1\\,{height ?? config.VideoMaxHeight}/ih)/2)*2:trunc(ih*min(1\\,{height ?? config.VideoMaxHeight}/ih)/2)*2,fps={N(info.Fps > 0 ? Math.Min(info.Fps, fps ?? config.VideoMaxFps) : fps ?? config.VideoMaxFps)}");
    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static Task<string> RunAsync(PluginConfig config, IEnumerable<string> args, CancellationToken token) =>
        ProcessRunner.RunFfmpegAsync(ExecutableLocator.Resolve(config.FfmpegPath, "ffmpeg"), args, "FFmpeg", config.FfmpegThreads, token);
}
