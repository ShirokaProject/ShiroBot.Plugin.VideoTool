using System.Globalization;
using System.Text.Json;

namespace ShiroBot.Plugin.VideoTool;

internal sealed record MediaInfo(double Duration, long Size, string Format, string VideoCodec, int Width, int Height,
    double Fps, string? AudioCodec, int SampleRate, int Channels)
{
    internal string Describe() => FormattableString.Invariant($"视频信息\n格式：{Format}\n时长：{TimeSpan.FromSeconds(Duration):hh\\:mm\\:ss\\.ff}\n大小：{Size / 1048576d:0.##} MiB\n画面：{Width}×{Height} · {VideoCodec} · {Fps:0.##} fps\n音频：{(AudioCodec is null ? "无" : $"{AudioCodec} · {SampleRate} Hz · {Channels} 声道")}");
}

internal static class MediaProbe
{
    internal static async Task<MediaInfo> ReadAsync(string input, PluginConfig config, CancellationToken token)
    {
        var json = await ProcessRunner.RunAsync(ExecutableLocator.ResolveProbe(config),
            ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_entries", "format=duration,size,format_name:stream=codec_type,codec_name,width,height,avg_frame_rate,sample_rate,channels,duration", "-of", "json", input], "ffprobe", token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => Text(s, "codec_type") == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("输入文件中没有视频轨道。");
        var audio = streams.FirstOrDefault(s => Text(s, "codec_type") == "audio");
        var format = root.GetProperty("format");
        var duration = Number(format, "duration");
        if (duration <= 0) duration = Number(video, "duration");
        if (!double.IsFinite(duration) || duration <= 0 || duration > 7 * 86400)
            throw new InvalidDataException("无法获取有效的视频时长。");
        return new(duration, new FileInfo(input).Length, Text(format, "format_name") ?? "未知", Text(video, "codec_name") ?? "未知",
            (int)Number(video, "width"), (int)Number(video, "height"), FrameRate(Text(video, "avg_frame_rate")),
            Text(audio, "codec_name"), (int)Number(audio, "sample_rate"), (int)Number(audio, "channels"));
    }

    private static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property.ToString() : null;

    private static double Number(JsonElement value, string name) =>
        double.TryParse(Text(value, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : 0;

    private static double FrameRate(string? text)
    {
        var parts = (text ?? "").Split('/');
        if (parts.Length != 2 || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator) || denominator <= 0) return 0;
        var fps = numerator / denominator;
        return double.IsFinite(fps) && fps > 0 ? fps : 0;
    }
}
