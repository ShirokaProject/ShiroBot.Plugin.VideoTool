using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShiroBot.Plugin.VideoTool;

internal sealed record EncoderProfile(string Name, string Label, string? Device = null, string? PixelFormat = null)
{
    internal string[] InputArguments => Name == "h264_vaapi" ? ["-vaapi_device", Device!] : [];
    internal string Filter(string filter) => Name == "h264_vaapi" ? filter + ",format=nv12,hwupload" : filter;
    internal string[] EncodingArguments => Name switch
    {
        "h264_vaapi" => ["-c:v", Name],
        "h264_videotoolbox" => ["-c:v", Name, "-pix_fmt", "yuv420p", "-allow_sw", "0"],
        "h264_mf" => ["-c:v", Name, "-pix_fmt", "nv12", "-hw_encoding", "1"],
        "h264_qsv" or "h264_rkmpp" => ["-c:v", Name, "-pix_fmt", "nv12"],
        _ => ["-c:v", Name, "-pix_fmt", PixelFormat ?? "yuv420p"]
    };
}

internal sealed record EncoderStatus(EncoderProfile Profile, bool Available, string Detail);
internal sealed record HardwareReport(DateTimeOffset TestedAt, IReadOnlyList<EncoderStatus> Encoders)
{
    internal string Describe() => "H.264 编码器实际探测（640×480，12 帧）\n" +
        string.Join('\n', Encoders.Select(s => $"{s.Profile.Label} / {s.Profile.Name}{(s.Profile.Device is null ? "" : " [" + s.Profile.Device + "]")}{(s.Profile.PixelFormat is null ? "" : " / " + s.Profile.PixelFormat)}：{(s.Available ? "可用" : "不可用")}，{s.Detail}")) +
        "\n短测不保证所有输入规格均可用；实际编码失败将回退 CPU。仅加速压缩编码，解码和缩放仍由 CPU 执行。";
}

internal static class HardwareEncoders
{
    internal static readonly (string Name, string Label)[] Known =
    [
        ("h264_nvenc", "NVIDIA NVENC"), ("h264_qsv", "Intel QSV"),
        ("h264_amf", "AMD AMF"), ("h264_videotoolbox", "Apple VideoToolbox"),
        ("h264_vaapi", "Linux VAAPI"), ("h264_v4l2m2m", "Linux V4L2 M2M（含高通驱动路径）"),
        ("h264_mf", "Windows Media Foundation（含高通）"),
        ("h264_rkmpp", "Rockchip MPP（厂商 FFmpeg）"), ("h264_omx", "OpenMAX IL（依赖厂商组件）")
    ];
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _cacheKey;
    private static HardwareReport? _cache;

    internal static async Task<HardwareReport> ProbeAsync(PluginConfig config, string directory, CancellationToken token, bool refresh = false)
    {
        var ffmpeg = ExecutableLocator.Resolve(config.FfmpegPath, "ffmpeg");
        var probe = ExecutableLocator.ResolveProbe(config);
        var key = $"{ffmpeg}|{File.GetLastWriteTimeUtc(ffmpeg).Ticks}|{probe}|{File.GetLastWriteTimeUtc(probe).Ticks}|{config.VaapiDevice}|{config.HardwareProbeSeconds}";
        await Gate.WaitAsync(token);
        try
        {
            if (!refresh && _cacheKey == key && _cache is { } cached && DateTimeOffset.UtcNow - cached.TestedAt < TimeSpan.FromMinutes(10)) return cached;
            var listing = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-encoders"], "FFmpeg 编码器列表", token);
            var names = ParseEncoderNames(listing);
            var statuses = new List<EncoderStatus>();
            var workspace = Path.Combine(directory, "hw-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try
            {
                // Raw source avoids relying on optional lavfi/testsrc filters in custom builds.
                var source = Path.Combine(workspace, "sample.yuv");
                var frame = new byte[640 * 480 * 3 / 2];
                for (var y = 0; y < 480; y++)
                    for (var x = 0; x < 640; x++) frame[y * 640 + x] = (byte)(16 + (x + y) % 220);
                Array.Fill(frame, (byte)128, 640 * 480, frame.Length - 640 * 480);
                await File.WriteAllBytesAsync(source, frame, token);
                foreach (var (name, label) in Known.Append(("libx264", "CPU libx264")))
                {
                    token.ThrowIfCancellationRequested();
                    if (!names.Contains(name)) { statuses.Add(new(new(name, label), false, "当前 FFmpeg 未编译此编码器")); continue; }
                    var profiles = Profiles(name, label, config).ToArray();
                    if (profiles.Length == 0)
                    {
                        statuses.Add(new(new(name, label), false, "没有可访问的 /dev/dri/renderD*；可配置 VAAPI 设备路径"));
                        continue;
                    }
                    foreach (var profile in profiles)
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(config.HardwareProbeSeconds));
                        try
                        {
                            var output = Path.Combine(workspace, "probe.mp4");
                            File.Delete(output);
                            var args = new List<string> { "-y", "-nostdin", "-v", "error" };
                            args.AddRange(profile.InputArguments);
                            args.AddRange(["-stream_loop", "-1", "-f", "rawvideo", "-pixel_format", "yuv420p", "-video_size", "640x480", "-framerate", "24", "-i", source,
                                "-frames:v", "12", "-an", "-vf", profile.Filter("null")]);
                            args.AddRange(profile.EncodingArguments);
                            args.AddRange(["-b:v", "1000000", output]);
                            await ProcessRunner.RunAsync(ffmpeg, args, name + " 测试", deadline.Token);
                            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new InvalidDataException("编码进程未生成输出。");
                            var json = await ProcessRunner.RunAsync(probe, ["-v", "error", "-select_streams", "v:0", "-count_frames", "-show_entries", "stream=codec_name,width,height,nb_read_frames", "-of", "json", output], "验证编码输出", deadline.Token);
                            ValidateProbeOutput(json);
                            await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-xerror", "-i", output, "-map", "0:v:0", "-f", "null", "-"], "验证解码", deadline.Token);
                            statuses.Add(new(profile, true, "编码、帧数与解码验证通过"));
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            statuses.Add(new(profile, false, $"探测超过 {config.HardwareProbeSeconds} 秒"));
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or JsonException)
                        {
                            var detail = error.Message.Replace('\n', ' ').Replace('\r', ' ');
                            statuses.Add(new(profile, false, detail.Length > 220 ? detail[..220] + "…" : detail));
                        }
                    }
                }
            }
            finally { Directory.Delete(workspace, recursive: true); }
            token.ThrowIfCancellationRequested();
            _cacheKey = key;
            return _cache = new(DateTimeOffset.UtcNow, statuses);
        }
        finally { Gate.Release(); }
    }

    internal static IEnumerable<EncoderProfile> Profiles(string name, string label, PluginConfig config)
    {
        if (name == "h264_v4l2m2m")
        {
            yield return new(name, label, PixelFormat: "nv12");
            yield return new(name, label, PixelFormat: "yuv420p");
            yield break;
        }
        if (name != "h264_vaapi") { yield return new(name, label); yield break; }
        if (!string.IsNullOrWhiteSpace(config.VaapiDevice)) { yield return new(name, label, config.VaapiDevice.Trim()); yield break; }
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/dev/dri")) yield break;
        foreach (var device in Directory.EnumerateFiles("/dev/dri", "renderD*").Order(StringComparer.Ordinal).Take(8)) yield return new(name, label, device);
    }

    internal static HashSet<string> ParseEncoderNames(string listing) => Regex.Matches(listing, @"(?m)^\s*V\S{5}\s+(\w+)\s")
        .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    internal static void ValidateProbeOutput(string json)
    {
        using var data = JsonDocument.Parse(json);
        if (!data.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array || streams.GetArrayLength() != 1)
            throw new InvalidDataException("探测输出没有唯一视频轨道。");
        var video = streams[0];
        if (!video.TryGetProperty("codec_name", out var codec) || codec.ToString() != "h264" ||
            !video.TryGetProperty("width", out var width) || !width.TryGetInt32(out var w) || w != 640 ||
            !video.TryGetProperty("height", out var height) || !height.TryGetInt32(out var h) || h != 480 ||
            !video.TryGetProperty("nb_read_frames", out var count) || !int.TryParse(count.ToString(), out var frames) || frames != 12)
            throw new InvalidDataException("探测输出格式、尺寸或帧数不符。");
    }

    internal static IReadOnlyList<EncoderProfile> Select(HardwareReport report, string selection) =>
        report.Encoders.Where(s => s.Available && s.Profile.Name != "libx264" && (selection == "auto" || s.Profile.Name == selection)).Select(s => s.Profile).ToArray();
}
