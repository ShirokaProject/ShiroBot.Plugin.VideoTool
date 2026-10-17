using ShiroBot.SDK.Config;

namespace ShiroBot.Plugin.VideoTool;

public sealed class PluginConfig
{
    [ConfigField("留空自动从宿主进程的 PATH 环境变量查找 ffmpeg；也可指定完整路径或程序名。不要加引号或启动参数。", Label = "FFmpeg 路径", Group = "basic", GroupLabel = "基础", GroupOrder = 0, Order = 0, Placeholder = "留空自动检测 PATH")]
    public string FfmpegPath { get; set; } = "";

    [ConfigField("留空自动从宿主进程的 PATH 环境变量查找 gifsicle；手动指定的路径优先。", Label = "gifsicle 路径", Group = "basic", GroupLabel = "基础", GroupOrder = 0, Order = 1, Placeholder = "留空自动检测 PATH")]
    public string GifsiclePath { get; set; } = "";

    [ConfigField("留空自动从 PATH 查找 ffprobe；也会尝试与已配置的 FFmpeg 相同的目录。用于视频信息、截取、压缩、封面和音频提取。", Label = "ffprobe 路径", Group = "basic", GroupLabel = "基础", GroupOrder = 0, Order = 2, Placeholder = "留空自动检测")]
    public string FfprobePath { get; set; } = "";

    [ConfigField("下载和转换共用的超时时间。", Label = "任务超时（秒）", Min = 10, Max = 3600, Group = "advanced", GroupLabel = "高级", GroupOrder = 4, Order = 2)]
    public int TimeoutSeconds { get; set; } = 180;

    [ConfigField("视频必须小于此大小；先检查文件消息大小，再检查下载响应与实际下载字节数。", Label = "最大输入大小（MiB）", Min = 1, Max = 2048, Group = "advanced", GroupLabel = "高级", GroupOrder = 4, Order = 3)]
    public int MaxInputMiB { get; set; } = 30;

    [ConfigField("输出文件超过此限制则不发送；平台可能还有更低的限制。", Label = "最大发送大小（MiB）", Min = 1, Max = 256, Group = "advanced", GroupLabel = "高级", GroupOrder = 4, Order = 4)]
    public int MaxOutputMiB { get; set; } = 30;

    [ConfigField("对应 -ss，从视频的这个时间开始转换。", Label = "起始时间（秒）", Min = 0, Max = 86400, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 0)]
    public double StartSeconds { get; set; } = 0.5;

    [ConfigField("0 表示转换到结尾，与默认命令一致；大于 0 时添加 -t。", Label = "截取时长（秒）", Min = 0, Max = 3600, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 1)]
    public double DurationSeconds { get; set; }

    [ConfigField("对应 fps。", Label = "帧率", Min = 1, Max = 60, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 2)]
    public int Fps { get; set; } = 12;

    [ConfigField("输出宽度。", Label = "宽度", Min = 16, Max = 2048, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 3)]
    public int Width { get; set; } = 512;

    [ConfigField("输出高度。", Label = "高度", Min = 16, Max = 2048, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 4)]
    public int Height { get; set; } = 512;

    [ConfigField("crop：放大后按裁剪位置取景，填满输出画布；pad：缩放后填充黑边。", Label = "画面适配", Options = ["crop", "pad"], Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 5)]
    public string FitMode { get; set; } = "crop";

    [ConfigField("仅 crop 生效。0 最左、50 居中、100 最右；输出尺寸不变。", Label = "水平裁剪位置（%）", Min = 0, Max = 100, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 14)]
    public double CropXPercent { get; set; } = 50;

    [ConfigField("仅 crop 生效。0 顶部、50 居中、100 底部；想截取靠上一点可设为 25。原画面无可裁剪的纵向余量时不生效。", Label = "垂直裁剪位置（%）", Min = 0, Max = 100, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 15)]
    public double CropYPercent { get; set; } = 50;

    [ConfigField("对应 scale 的 flags。", Label = "缩放算法", Options = ["lanczos", "bicubic", "bilinear", "neighbor", "area", "spline"], Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 6)]
    public string ScaleAlgorithm { get; set; } = "lanczos";

    [ConfigField("对应 palettegen 的 max_colors。", Label = "调色板颜色数", Min = 4, Max = 256, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 7)]
    public int MaxColors { get; set; } = 128;

    [ConfigField("diff：侧重帧间变化区域；full：统计全部像素。", Label = "调色板统计", Options = ["diff", "full"], Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 8)]
    public string StatsMode { get; set; } = "diff";

    [ConfigField("对应 paletteuse 的 dither。", Label = "抖动算法", Options = ["bayer", "none", "heckbert", "floyd_steinberg", "sierra2", "sierra2_4a"], Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 9)]
    public string Dither { get; set; } = "bayer";

    [ConfigField("仅在选择 bayer 时生效，对应 bayer_scale。", Label = "Bayer 强度", Min = 0, Max = 5, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 10)]
    public int BayerScale { get; set; } = 4;

    [ConfigField("0 无限循环，-1 不循环，正整数为重复次数。", Label = "循环次数", Min = -1, Max = 65535, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 11)]
    public int Loop { get; set; }

    [ConfigField("对应 gifsicle -O，0 关闭优化，3 为最高级。", Label = "GIF 优化等级", Min = 0, Max = 3, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 12)]
    public int OptimizationLevel { get; set; } = 3;

    [ConfigField("对应 gifsicle --lossy，0 禁用有损压缩；越大通常越小，失真也越明显。", Label = "有损压缩强度", Min = 0, Max = 1000, Group = "gif", GroupLabel = "GIF", GroupOrder = 1, Order = 13)]
    public int Lossy { get; set; } = 160;

    [ConfigField("截取和压缩视频的最大高度；不放大较小的视频，宽度保持比例。", Label = "视频最大高度", Min = 16, Max = 2160, Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 0)]
    public int VideoMaxHeight { get; set; } = 720;

    [ConfigField("auto 探测硬件；指定接口名按压缩格式自动映射为 H.264 或 HEVC 编码器。cpu 表示主动使用软件编码，与回退开关无关。", Label = "压缩编码器", Options = ["auto", "cpu", "h264_nvenc", "h264_qsv", "h264_amf", "h264_videotoolbox", "h264_vaapi", "h264_v4l2m2m", "h264_mf", "h264_rkmpp", "h264_omx"], Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 7)]
    public string CompressionEncoder { get; set; } = "auto";

    [ConfigField("HEVC 通常压缩效率较高，但需要播放器及平台支持。命令 vcompress 10 hevc 可临时覆盖。", Label = "压缩输出格式", Options = ["h264", "hevc"], Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 9)]
    public string CompressionCodec { get; set; } = "h264";

    [ConfigField("默认关闭：硬件失败或无法达到目标时直接报错，不启动软件编码或其探测。打开后允许 libx264/libx265 回退。解码、缩放和音频仍会用 CPU。", Label = "允许 CPU 编码回退", Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 10)]
    public bool AllowCpuFallback { get; set; }

    [ConfigField("限制 FFmpeg 解码、编码及滤镜线程数；不是 CPU 使用率硬限制。", Label = "FFmpeg 线程数", Min = 1, Max = 16, Group = "advanced", GroupLabel = "高级", GroupOrder = 4, Order = 7)]
    public int FfmpegThreads { get; set; } = 2;

    internal PluginConfig ForCompressionCommand(VideoCommand command)
    {
        var copy = (PluginConfig)MemberwiseClone();
        if (command.Codec is { } codec) copy.CompressionCodec = codec;
        return copy;
    }

    [ConfigField("留空枚举 /dev/dri/renderD*；多显卡可指定一个设备，例如 /dev/dri/renderD129。适用于 VAAPI。V4L2 M2M 由 FFmpeg 自动寻找编码设备。", Label = "VAAPI 设备路径", Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 8)]
    public string VaapiDevice { get; set; } = "";

    [ConfigField("每个编码器/设备的实际编码与验证超时。总耗时仍受任务超时约束；探测结果缓存 10 分钟。", Label = "硬件探测超时（秒）", Min = 2, Max = 60, Group = "advanced", GroupLabel = "高级", GroupOrder = 4, Order = 6)]
    public int HardwareProbeSeconds { get; set; } = 10;

    [ConfigField("截取和压缩视频的帧率上限。", Label = "视频最大帧率", Min = 1, Max = 60, Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 1)]
    public int VideoMaxFps { get; set; } = 30;

    [ConfigField("H.264 编码速度；较慢通常压缩效率更好。", Label = "视频编码预设", Options = ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow"], Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 2)]
    public string VideoPreset { get; set; } = "fast";

    [ConfigField("截取片段的画质，数值越小质量越高、体积通常越大；目标大小压缩不使用此值。", Label = "截取视频 CRF", Min = 0, Max = 40, Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 3)]
    public int VideoCrf { get; set; } = 23;

    [ConfigField("vcompress 不带大小参数时的默认目标。", Label = "默认压缩目标（MiB）", Min = 0.1, Max = 256, Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 4)]
    public double CompressionTargetMiB { get; set; } = 10;

    [ConfigField("一次截取允许的最长时长。", Label = "最大截取时长（秒）", Min = 1, Max = 3600, Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 5)]
    public int MaxClipSeconds { get; set; } = 300;

    [ConfigField("JPEG 截图质量，数值越小质量越高。", Label = "封面 JPEG 质量", Min = 2, Max = 31, Group = "video", GroupLabel = "视频", GroupOrder = 2, Order = 6)]
    public int CoverQuality { get; set; } = 2;

    [ConfigField("提取 MP3 文件的比特率。", Label = "MP3 比特率（kbps）", Min = 32, Max = 320, Group = "audio", GroupLabel = "音频", GroupOrder = 3, Order = 0)]
    public int Mp3BitrateKbps { get; set; } = 192;

    [ConfigField("截取片段的 AAC 比特率；目标大小压缩时为音频比特率上限。", Label = "视频音频比特率（kbps）", Min = 32, Max = 320, Group = "audio", GroupLabel = "音频", GroupOrder = 3, Order = 1)]
    public int VideoAudioKbps { get; set; } = 128;

    internal PluginConfig ForGifCommand(VideoCommand command)
    {
        var copy = (PluginConfig)MemberwiseClone();
        if (command.CropOffset is { } offset)
        {
            copy.FitMode = "crop";
            copy.CropXPercent = 50 + offset;
            copy.CropYPercent = 50 + offset;
        }
        return copy;
    }

    internal void Validate()
    {
        Range(TimeoutSeconds, 10, 3600, "任务超时");
        Range(MaxInputMiB, 1, 2048, "输入大小");
        Range(MaxOutputMiB, 1, 256, "发送大小");
        Range(StartSeconds, 0, 86400, "起始时间");
        Range(DurationSeconds, 0, 3600, "截取时长");
        Range(Fps, 1, 60, "帧率");
        Range(Width, 16, 2048, "宽度");
        Range(Height, 16, 2048, "高度");
        Range(MaxColors, 4, 256, "颜色数");
        Range(BayerScale, 0, 5, "Bayer 强度");
        Range(Loop, -1, 65535, "循环次数");
        Range(OptimizationLevel, 0, 3, "优化等级");
        Range(Lossy, 0, 1000, "有损压缩强度");
        Range(CropXPercent, 0, 100, "水平裁剪位置");
        Range(CropYPercent, 0, 100, "垂直裁剪位置");
        Range(VideoMaxHeight, 16, 2160, "视频最大高度");
        Range(HardwareProbeSeconds, 2, 60, "硬件探测超时");
        Range(FfmpegThreads, 1, 16, "FFmpeg 线程数");
        Choice(CompressionCodec, ["h264", "hevc"], "压缩输出格式");
        Choice(CompressionEncoder, ["auto", "cpu", .. HardwareEncoders.Known.Select(x => x.Name)], "压缩编码器");
        Range(VideoMaxFps, 1, 60, "视频最大帧率");
        Range(VideoCrf, 0, 40, "视频 CRF");
        Range(CompressionTargetMiB, 0.1, 256, "默认压缩目标");
        Range(MaxClipSeconds, 1, 3600, "最大截取时长");
        Range(CoverQuality, 2, 31, "封面质量");
        Range(Mp3BitrateKbps, 32, 320, "MP3 比特率");
        Range(VideoAudioKbps, 32, 320, "视频音频比特率");
        Choice(VideoPreset, ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow"], "视频预设");
        Choice(FitMode, ["crop", "pad"], "画面适配");
        Choice(ScaleAlgorithm, ["lanczos", "bicubic", "bilinear", "neighbor", "area", "spline"], "缩放算法");
        Choice(StatsMode, ["diff", "full"], "调色板统计");
        Choice(Dither, ["bayer", "none", "heckbert", "floyd_steinberg", "sierra2", "sierra2_4a"], "抖动算法");
    }

    private static void Range(double value, double min, double max, string label)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{label}应在 {min} 到 {max} 之间。");
    }

    private static void Choice(string value, string[] options, string label)
    {
        if (!options.Contains(value, StringComparer.Ordinal))
            throw new ArgumentException($"{label}选项无效。");
    }
}
