using System.Globalization;

namespace ShiroBot.Plugin.VideoTool;

internal enum ToolOperation { Gif, Clip, Mp3, Compress, Cover, Info, Help, Hardware }

internal sealed record VideoCommand(ToolOperation Operation, double Start = 0, double End = 0,
    double? TargetMiB = null, string? Url = null, bool QuoteOnly = true, double? CropOffset = null, string? Codec = null)
{
    internal const string Help = "引用视频或带视频后缀的文件，回复：\n2gif — 居中转 GIF\n2gif -25 / 25 — 横屏偏左/右，竖屏偏上/下；-50/50 到边缘\nvclip 00:05 00:15 — 截取片段\n2mp3 — 提取 MP3 文件\nvcompress [目标MiB] [h264|hevc] — 压缩视频，例如 vcompress 10 hevc\nvcover [时间] — 截取封面，默认第 0 秒\nvinfo — 查看视频信息\nvhw — 实测硬件编码器，无需引用\nvhelp — 查看帮助\n时间可用秒数、分:秒或时:分:秒。";

    internal static VideoCommand? Parse(string text)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var key = parts[0].ToLowerInvariant();
        switch (key)
        {
            case "2gif":
                if (parts.Length > 2) throw CropUsage();
                return new(ToolOperation.Gif, CropOffset: parts.Length == 1 ? 0 : ParseOffset(parts[1]));
            case "#gif" when parts.Length <= 2: return new(ToolOperation.Gif, Url: parts.ElementAtOrDefault(1), QuoteOnly: false);
            case "2mp3" when parts.Length == 1: return new(ToolOperation.Mp3);
            case "vinfo" when parts.Length == 1: return new(ToolOperation.Info);
            case "vhw" when parts.Length == 1: return new(ToolOperation.Hardware);
            case "vhelp" when parts.Length == 1: return new(ToolOperation.Help);
            case "vcover":
                if (parts.Length > 2) throw new ArgumentException("用法：vcover [时间]，例如 vcover 00:03。");
                return new(ToolOperation.Cover, Start: parts.Length == 2 ? ParseTime(parts[1]) : 0);
            case "vclip":
                if (parts.Length != 3) throw new ArgumentException("用法：vclip <开始时间> <结束时间>，例如 vclip 00:05 00:15。");
                var start = ParseTime(parts[1]);
                var end = ParseTime(parts[2]);
                if (end <= start) throw new ArgumentException("结束时间必须大于开始时间。");
                return new(ToolOperation.Clip, start, end);
            case "vcompress":
                if (parts.Length == 1) return new(ToolOperation.Compress);
                if (parts.Length is < 2 or > 3 || !double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size < 0.1 || size > 256)
                    throw new ArgumentException("用法：vcompress [目标MiB] [h264|hevc]，例如 vcompress 10 hevc。大小范围 0.1 到 256 MiB。");
                var codec = parts.Length == 3 ? parts[2].ToLowerInvariant() : null;
                if (codec is not (null or "h264" or "hevc")) throw new ArgumentException("压缩格式只支持 h264 或 hevc。");
                return new(ToolOperation.Compress, TargetMiB: size, Codec: codec);
            default: return null;
        }
    }

    private static ArgumentException CropUsage() => new("用法：2gif [偏移]，例如 2gif -25 或 2gif 25。横屏左右、竖屏上下；范围 -50 到 50，0 居中，-50 最左/上，50 最右/下。");

    private static double ParseOffset(string value)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < -50 || number > 50)
            throw CropUsage();
        return number;
    }

    internal static double ParseTime(string text)
    {
        var parts = text.Split(':');
        if (parts.Length is < 1 or > 3) throw new ArgumentException("时间格式无效，请使用秒数、分:秒或时:分:秒。");
        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var style = i == parts.Length - 1 ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
            if (!double.TryParse(parts[i], style, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0 || (parts.Length > 1 && i > 0 && value >= 60))
                throw new ArgumentException("时间格式无效，分、秒字段须在 0 到 59 之间。");
            total = total * 60 + value;
        }
        if (total > 86400) throw new ArgumentException("时间不能超过 24 小时。");
        return total;
    }
}
