using System.Globalization;
using System.Text.Json;
using ShiroBot.Plugin.VideoTool;

internal static class VideoOperationsVerification
{
    internal static async Task<string> RunAsync(string root, string silentInput, string ffmpeg, CancellationToken token)
    {
        Check(VideoCommand.Parse("2gif")?.Operation == ToolOperation.Gif && VideoCommand.Parse("2mp3")?.Operation == ToolOperation.Mp3, "Legacy GIF and new audio commands parse");
        Check(VideoCommand.Parse("vclip 00:00.5 00:01.5") is { Start: 0.5, End: 1.5 }, "Clip times support fractional seconds");
        Check(VideoCommand.ParseTime("01:02:03.5") == 3723.5, "Hour/minute/second times parse correctly");
        foreach (var invalid in new[] { "vclip 3 2", "vclip 1", "vcover -1", "vcover 00:60", "vcompress NaN", "vcompress -1" })
            await Reject<ArgumentException>(() => Task.Run(() => VideoCommand.Parse(invalid)), "Invalid command rejected: " + invalid);
        Check(VideoCommand.Parse("vcompressor") is null && VideoCommand.Parse("#gift") is null, "Unrelated command prefixes are ignored");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(VideoCommand.Parse("vcompress 0.12")?.TargetMiB == 0.12, "Compression size parsing is culture independent");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        var directory = Path.Combine(root, "video operations");
        Directory.CreateDirectory(directory);
        var config = new PluginConfig
        {
            FfmpegPath = ffmpeg, VideoMaxHeight = 180, VideoMaxFps = 12, VideoPreset = "veryfast",
            GifsiclePath = Path.Combine(root, "absent gifsicle"), CompressionTargetMiB = 0.12, CompressionEncoder = "cpu"
        };
        var input = Path.Combine(directory, "source with audio.mp4");
        await ProcessRunner.RunAsync(ffmpeg,
            ["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "4", "-c:v", "libx264", "-preset", "veryfast", "-crf", "8", "-c:a", "aac", "-b:a", "128k", input], "test source", token);
        var info = await MediaProbe.ReadAsync(input, config, token);
        Check(info.VideoCodec == "h264" && info.AudioCodec == "aac" && info.Width == 640 && info.Height == 360 && Math.Abs(info.Duration - 4) < 0.1, "ffprobe reads real video and audio metadata");
        var description = await VideoOperations.ExecuteAsync(new(ToolOperation.Info), input, directory, config, token);
        Check(description.Kind == OutputKind.Text && description.Text!.Contains("640×360") && description.Text.Contains("aac"), "vinfo returns readable media information without gifsicle");

        var clip = await VideoOperations.ExecuteAsync(new(ToolOperation.Clip, 0.5, 1.5), input, directory, config, token);
        var clipped = await MediaProbe.ReadAsync(clip.Path!, config, token);
        Check(clip.Kind == OutputKind.Video && Math.Abs(clipped.Duration - 1) < 0.15 && clipped.Width == 320 && clipped.Height == 180 && clipped.Fps == 12 && clipped.AudioCodec == "aac", "Real clip preserves audio and follows resolution/fps settings");

        var mp3 = await VideoOperations.ExecuteAsync(new(ToolOperation.Mp3), input, directory, config, token);
        var audioJson = await ProcessRunner.RunAsync(ExecutableLocator.ResolveProbe(config), ["-v", "error", "-show_entries", "stream=codec_name,codec_type", "-of", "json", mp3.Path!], "probe MP3", token);
        using (var document = JsonDocument.Parse(audioJson))
        {
            var streams = document.RootElement.GetProperty("streams");
            Check(mp3.Kind == OutputKind.File && streams.GetArrayLength() == 1 && streams[0].GetProperty("codec_name").GetString() == "mp3", "2mp3 produces an actual MP3 file without a video track");
        }

        var cover = await VideoOperations.ExecuteAsync(new(ToolOperation.Cover, Start: 1), input, directory, config, token);
        var bytes = await File.ReadAllBytesAsync(cover.Path!, token);
        Check(cover.Kind == OutputKind.Image && bytes[0] == 0xff && bytes[1] == 0xd8, "vcover produces a real JPEG");
        await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-i", cover.Path!, "-f", "null", "-"], "decode JPEG", token);

        Check(info.Size > 0.12 * 1048576, "Compression sample exceeds the target size");
        var compressed = await VideoOperations.ExecuteAsync(new(ToolOperation.Compress), input, directory, config, token);
        var compressedInfo = await MediaProbe.ReadAsync(compressed.Path!, config, token);
        Check(compressed.Kind == OutputKind.Video && compressedInfo.Size <= 0.12 * 1048576 && compressedInfo.AudioCodec == "aac" && Math.Abs(compressedInfo.Duration - info.Duration) < 0.15, "Two-pass compression meets the configured target and preserves duration/audio");
        Check(!Directory.EnumerateFiles(directory, "video-pass*").Any(), "Compression pass logs are removed");
        config.CompressionEncoder = "auto";
        var accelerated = await VideoOperations.ExecuteAsync(new(ToolOperation.Compress), input, directory, config, token);
        Check(new FileInfo(accelerated.Path!).Length <= 0.12 * 1048576, "Auto hardware compression or fallback honors target size");
        Console.WriteLine("Actual auto compression encoder: " + accelerated.Text);
        config.CompressionEncoder = "h264_v4l2m2m";
        var fallback = await VideoOperations.ExecuteAsync(new(ToolOperation.Compress), input, directory, config, token);
        Check(new FileInfo(fallback.Path!).Length <= 0.12 * 1048576, "Selected V4L2 encoder either encodes successfully or falls back to CPU within target");
        if (!OperatingSystem.IsWindows())
        {
            var wrapper = Path.Combine(directory, "probe succeeds real hardware fails");
            var marker = Path.Combine(directory, "hardware-failure-observed");
            static string Q(string path) => "'" + path.Replace("'", "'\\''") + "'";
            var script = "#!/bin/sh\ncase \"$*\" in\n" +
                " *-encoders*) printf ' V..... h264_nvenc simulated\\n V..... libx264 CPU\\n'; exit 0;;\n" +
                "esac\ncase \"$*\" in\n *'-c:v h264_nvenc'*)\n" +
                "  case \"$*\" in\n   *'-frames:v 12'*) for output; do :; done\n" +
                "    exec " + Q(ffmpeg) + " -y -v error -f lavfi -i testsrc2=size=640x480:rate=24 -frames:v 12 -c:v libx264 \"$output\";;\n" +
                "   *) echo observed > " + Q(marker) + "; echo 'simulated device became busy' >&2; exit 1;;\n  esac;;\nesac\nexec " + Q(ffmpeg) + " \"$@\"\n";
            await File.WriteAllTextAsync(wrapper, script, token);
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            config.FfmpegPath = wrapper;
            config.FfprobePath = ExecutableLocator.ResolveProbe(new PluginConfig { FfmpegPath = ffmpeg });
            config.CompressionEncoder = "h264_nvenc";
            var busy = await VideoOperations.ExecuteAsync(new(ToolOperation.Compress), input, directory, config, token);
            Check(File.Exists(marker) && busy.Text == "libx264" && new FileInfo(busy.Path!).Length <= 0.12 * 1048576,
                "Hardware passing probe but failing actual encode automatically falls back to CPU within target");
            config.FfmpegPath = ffmpeg;
            config.FfprobePath = "";
        }
        var skipped = await VideoOperations.ExecuteAsync(new(ToolOperation.Compress, TargetMiB: 10), input, directory, config, token);
        Check(skipped.Kind == OutputKind.Text && skipped.Text!.Contains("无需压缩"), "Already-small videos skip unnecessary re-encoding");
        await Reject<InvalidDataException>(() => VideoOperations.ExecuteAsync(new(ToolOperation.Mp3), silentInput, directory, config, token), "Silent video reports missing audio");
        await Reject<ArgumentException>(() => VideoOperations.ExecuteAsync(new(ToolOperation.Cover, Start: 20), input, directory, config, token), "Out-of-range cover time is rejected");
        await Reject<ArgumentException>(() => VideoOperations.ExecuteAsync(new(ToolOperation.Clip, 0, 20), input, directory, config, token), "Out-of-range clip time is rejected");
        await Reject<ArgumentException>(() => VideoOperations.ExecuteAsync(new(ToolOperation.Compress, TargetMiB: 256), input, directory, config, token), "Compression target cannot exceed the sending limit");
        return input;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    private static async Task Reject<T>(Func<Task> action, string label) where T : Exception
    {
        try { await action(); }
        catch (T) { Console.WriteLine("PASS " + label); return; }
        throw new Exception(label);
    }
}
