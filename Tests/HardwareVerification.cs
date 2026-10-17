using ShiroBot.Plugin.VideoTool;

internal static class HardwareVerification
{
    internal static async Task RunAsync(string root, string ffmpeg, CancellationToken token)
    {
        var config = new PluginConfig { FfmpegPath = ffmpeg, AllowCpuFallback = true };
        var names = HardwareEncoders.ParseEncoderNames(" V....D h264_v4l2m2m sample\n V....D libx264 sample\n A..... aac audio\n");
        Check(names.SetEquals(["h264_v4l2m2m", "libx264"]), "Encoder listing parser excludes audio and headers");
        var vaapi = new EncoderProfile("h264_vaapi", "VAAPI", "/dev/dri/renderD129");
        Check(vaapi.InputArguments.SequenceEqual(["-vaapi_device", "/dev/dri/renderD129"]) && vaapi.Filter("scale=640:480").EndsWith("format=nv12,hwupload"), "VAAPI binds its device and uploads NV12 frames");
        var formats = HardwareEncoders.Profiles("h264_v4l2m2m", "Qualcomm", config).ToArray();
        Check(formats.Select(p => p.PixelFormat).SequenceEqual(["nv12", "yuv420p"]), "V4L2 supports NV12 and YUV420P upload paths");
        Check(new EncoderProfile("h264_mf", "MF").EncodingArguments.Contains("-hw_encoding") &&
            new EncoderProfile("h264_videotoolbox", "VT").EncodingArguments.TakeLast(2).SequenceEqual(["-allow_sw", "0"]), "Media Foundation and VideoToolbox require hardware rather than silent software fallback");
        foreach (var (name, label) in HardwareEncoders.Known)
        {
            var profile = new EncoderProfile(name, label, name == "h264_vaapi" ? "/dev/dri/renderD128" : null);
            Check(profile.EncodingArguments.Contains(name) && !profile.EncodingArguments.Contains("-pass") && !profile.EncodingArguments.Contains("-crf"), "Hardware profile does not reuse x264 pass/CRF flags: " + name);
        }
        var fakeReport = new HardwareReport(DateTimeOffset.UtcNow,
            [new(new("h264_nvenc", "NVIDIA"), false, "driver unavailable"), new(new("h264_v4l2m2m", "V4L2"), true, "ok"), new(new("libx264", "CPU"), true, "ok")]);
        Check(HardwareEncoders.Select(fakeReport, "auto").Single().Name == "h264_v4l2m2m" && HardwareEncoders.Select(fakeReport, "h264_nvenc").Count == 0, "Selection rejects a compiled encoder that failed its test");
        try
        {
            HardwareEncoders.ValidateProbeOutput("{\"streams\":[{\"codec_name\":\"h264\",\"width\":640,\"height\":480,\"nb_read_frames\":\"0\"}]}");
            throw new Exception("Empty output accepted");
        }
        catch (InvalidDataException) { Console.WriteLine("PASS Probe rejects zero decoded frames"); }
        var report = await HardwareEncoders.ProbeAsync(config, root, token, refresh: true);
        Check(report.Encoders.Any(s => s.Profile.Name == "libx264" && s.Available), "Real CPU reference encode and decode verified");
        Check(ReferenceEquals(report, await HardwareEncoders.ProbeAsync(config, root, token)), "Repeated probe reuses cached actual results");
        Check(!Directory.EnumerateDirectories(root, "hw-probe-*").Any(), "Probe temporary samples and outputs removed");
        Console.WriteLine(report.Describe());
        var strict = new PluginConfig { FfmpegPath = ffmpeg, CompressionCodec = "hevc", AllowCpuFallback = false };
        var hevc = await HardwareEncoders.ProbeAsync(strict, root, token, refresh: true);
        Check(hevc.Codec == "hevc" && hevc.Encoders.All(s => !s.Profile.Name.StartsWith("libx", StringComparison.Ordinal)), "Hardware-only HEVC probe never tests a software encoder");
        Check(HardwareEncoders.Candidates(strict).All(s => s.Name.StartsWith("hevc_", StringComparison.Ordinal)), "HEVC uses independently tested HEVC hardware candidates");
        Console.WriteLine(hevc.Describe());

        // A fake executable tests the crucial case: listed does not mean usable.
        if (!OperatingSystem.IsWindows())
        {
            var executable = Path.Combine(root, "fake hardware ffmpeg");
            await File.WriteAllTextAsync(executable, "#!/bin/sh\ncase \"$*\" in\n *-encoders*) printf ' V..... h264_nvenc simulated\\n';;\n *) exec /bin/sleep 30;;\nesac\n", token);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var timeoutConfig = new PluginConfig { FfmpegPath = executable, FfprobePath = ExecutableLocator.ResolveProbe(config), HardwareProbeSeconds = 2 };
            var timed = await HardwareEncoders.ProbeAsync(timeoutConfig, root, token, refresh: true);
            Check(timed.Encoders.Single(s => s.Profile.Name == "h264_nvenc") is { Available: false } status && status.Detail.Contains("超过"), "Listed but hung encoder times out and is never selected");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await HardwareEncoders.ProbeAsync(config, root, cancelled.Token, refresh: true); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS Hardware probe propagates caller cancellation"); }
            await HardwareEncoders.ProbeAsync(config, root, token, refresh: true);
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
}
