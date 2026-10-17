using System.Diagnostics;
using System.Globalization;
using System.Text;
using ShiroBot.Plugin.VideoTool;
using ShiroBot.SDK.Config;

var root = Path.Combine(Path.GetTempPath(), "video to gif verification " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var config = new PluginConfig
    {
        FfmpegPath = args.ElementAtOrDefault(0) ?? "ffmpeg",
        GifsiclePath = args.ElementAtOrDefault(1) ?? "gifsicle"
    };
    ExecutableLocatorVerification.Run(root);
    Check(new PluginConfig().MaxInputMiB == 30, "Default input size limit is 30 MiB");
    Check(VideoToolPlugin.IsVideo(new ShiroBot.SDK.Models.FileSegment("") { FileName = "clip.MOV" }) &&
          VideoToolPlugin.IsVideo(new ShiroBot.SDK.Models.VideoSegment("")) &&
          !VideoToolPlugin.IsVideo(new ShiroBot.SDK.Models.FileSegment("") { FileName = "clip.mp4.exe" }), "Video segments and video file suffixes are recognized safely");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
    var token = deadline.Token;
    const string expected = "[0:v]fps=12,scale=512:512:force_original_aspect_ratio=increase:flags=lanczos,crop=512:512,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4";
    Check(GifConverter.BuildFilter(config) == expected, "Default filter matches the supplied command");
    var previousCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var arguments = GifConverter.FfmpegArguments(config, "/tmp/input with spaces.mp4", "/tmp/output with spaces.gif");
        Check(arguments[Array.IndexOf(arguments, "-ss") + 1] == "0.5", "Decimal arguments are culture independent");
        Check(arguments.Contains("/tmp/input with spaces.mp4"), "Paths are individual process arguments");
    }
    finally { CultureInfo.CurrentCulture = previousCulture; }
    Check(GifConverter.GifsicleArguments(config, "a.gif", "b.gif").SequenceEqual(new[] { "-O3", "--lossy=160", "a.gif", "-o", "b.gif" }), "Default gifsicle arguments match");
    var fields = typeof(PluginConfig).GetProperties().Select(p => p.GetCustomAttributes(typeof(ConfigFieldAttribute), false).Cast<ConfigFieldAttribute>().Single()).ToArray();
    Check(fields.Count(f => f.Group == "gif" && f.GroupLabel == "GIF") == 16 && fields.Select(f => f.Group).Distinct().Count() == 5, "WebUI exposes basic, GIF, video, audio and advanced categories");
    await Expect<ArgumentException>(() => Task.Run(() => GifConverter.BuildFilter(new() { Dither = "bayer;movie=secret" })), "Invalid filter option rejected");
    await Expect<ArgumentException>(() => Task.Run(() => GifConverter.BuildFilter(new() { StartSeconds = double.NaN })), "Non-finite time rejected");

    var input = Path.Combine(root, "source with spaces.mp4");
    await GifConverter.RunAsync(config.FfmpegPath, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=192x108:rate=24", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", input], "test source", token);
    var previousPath = Environment.GetEnvironmentVariable("PATH");
    var configuredFfmpeg = ExecutableLocator.Resolve(config.FfmpegPath, "ffmpeg");
    var configuredGifsicle = ExecutableLocator.Resolve(config.GifsiclePath, "gifsicle");
    string defaultOutput;
    try
    {
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, Path.GetDirectoryName(configuredFfmpeg), Path.GetDirectoryName(configuredGifsicle), previousPath));
        config.FfmpegPath = "";
        config.GifsiclePath = "";
        defaultOutput = await GifConverter.ConvertAsync(input, root, config, token);
        Check(File.Exists(defaultOutput), "Empty tool paths auto-detect PATH and perform a real conversion");
    }
    finally
    {
        Environment.SetEnvironmentVariable("PATH", previousPath);
        config.FfmpegPath = configuredFfmpeg;
        config.GifsiclePath = configuredGifsicle;
    }
    var gif = InspectGif(defaultOutput);
    Check(gif.Width == 512 && gif.Height == 512 && gif.Frames == 18 && gif.Loop == 0, "Real default conversion: 512×512, 18 frames after 0.5s, infinite loop");
    Check(Math.Abs(gif.Seconds - 1.5) < 0.03, "12 fps GIF timing is preserved within centisecond precision");
    Check(!File.Exists(Path.Combine(root, "temp_base.gif")), "Intermediate GIF removed after success");
    await GifConverter.RunAsync(config.FfmpegPath, ["-v", "error", "-i", defaultOutput, "-f", "null", "-"], "decode result", token);

    config.StartSeconds = 0.25;
    config.DurationSeconds = 0.5;
    config.Width = 96;
    config.Height = 64;
    config.Fps = 8;
    config.FitMode = "pad";
    config.Dither = "none";
    config.MaxColors = 32;
    config.StatsMode = "full";
    config.Loop = -1;
    config.OptimizationLevel = 1;
    config.Lossy = 0;
    var custom = InspectGif(await GifConverter.ConvertAsync(input, root, config, token));
    Check(custom.Width == 96 && custom.Height == 64 && custom.Frames == 4 && custom.Loop is null, "Custom padding, duration, fps, palette, no loop and lossless settings work");
    await GifConverter.RunAsync(config.FfmpegPath, ["-v", "error", "-i", defaultOutput, "-f", "null", "-"], "decode custom result", token);

    var invalidDir = Path.Combine(root, "invalid");
    Directory.CreateDirectory(invalidDir);
    var invalid = Path.Combine(invalidDir, "invalid.mp4");
    await File.WriteAllTextAsync(invalid, "not a video", token);
    await Expect<InvalidOperationException>(() => GifConverter.ConvertAsync(invalid, invalidDir, config, token), "Corrupt input reports FFmpeg failure");
    Check(!File.Exists(Path.Combine(invalidDir, "temp_base.gif")) && !File.Exists(Path.Combine(invalidDir, "output.gif")), "Failure removes partial GIFs");
    await Expect<InvalidOperationException>(() => GifConverter.RunAsync(Path.Combine(root, "missing ffmpeg"), [], "FFmpeg", token), "Missing executable reports a configuration error");
    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    var watch = Stopwatch.StartNew();
    await Expect<OperationCanceledException>(() => GifConverter.RunAsync(config.FfmpegPath, ["-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=32x32:rate=1", "-t", "30", "-f", "null", "-"], "cancel test", cancel.Token), "Cancellation stops the actual FFmpeg process");
    Check(watch.Elapsed < TimeSpan.FromSeconds(5), "Cancellation returns promptly");
    await CropVerification.RunAsync(root, config.FfmpegPath, config.GifsiclePath, token);
    await HardwareVerification.RunAsync(root, config.FfmpegPath, token);
    var audioInput = await VideoOperationsVerification.RunAsync(root, input, config.FfmpegPath, token);
    await PluginVerification.RunAsync(root, input, audioInput, config.FfmpegPath, config.GifsiclePath, token);
    await LifecycleVerification.RunAsync(root, input, config.FfmpegPath, config.GifsiclePath, token);
    Console.WriteLine("All verification checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}

static async Task Expect<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine("PASS " + label); return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + label);
}

static (int Width, int Height, int Frames, double Seconds, int? Loop) InspectGif(string path)
{
    using var reader = new BinaryReader(File.OpenRead(path));
    Check(Encoding.ASCII.GetString(reader.ReadBytes(6)) is "GIF89a" or "GIF87a", "GIF signature valid");
    var width = reader.ReadUInt16();
    var height = reader.ReadUInt16();
    var packed = reader.ReadByte();
    reader.ReadBytes(2);
    if ((packed & 128) != 0) reader.ReadBytes(3 * (1 << ((packed & 7) + 1)));
    var frames = 0;
    var centiseconds = 0;
    int? loop = null;
    while (true)
    {
        var marker = reader.ReadByte();
        if (marker == 0x3b) break;
        if (marker == 0x2c)
        {
            reader.ReadBytes(8);
            packed = reader.ReadByte();
            if ((packed & 128) != 0) reader.ReadBytes(3 * (1 << ((packed & 7) + 1)));
            reader.ReadByte(); // LZW minimum code size
            Blocks(reader);
            frames++;
        }
        else if (marker == 0x21)
        {
            var label = reader.ReadByte();
            var blocks = Blocks(reader);
            if (label == 0xf9) centiseconds += blocks[1] | blocks[2] << 8;
            if (label == 0xff && Encoding.ASCII.GetString(blocks, 0, 11) == "NETSCAPE2.0")
                loop = blocks[12] | blocks[13] << 8;
        }
        else throw new InvalidDataException($"Unexpected GIF block {marker}");
    }
    return (width, height, frames, centiseconds / 100d, loop);
}

static byte[] Blocks(BinaryReader reader)
{
    using var bytes = new MemoryStream();
    int length;
    while ((length = reader.ReadByte()) != 0) bytes.Write(reader.ReadBytes(length));
    return bytes.ToArray();
}
