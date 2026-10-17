using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ShiroBot.Plugin.VideoTool;

internal static class ProcessRunner
{
    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, string name, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException($"无法启动 {name}，请检查配置路径和执行权限。", error);
        }
        var stderr = ReadAsync(process.StandardError, 4096, tail: true);
        var stdout = ReadAsync(process.StandardOutput, 1024 * 1024, tail: false);
        try { await process.WaitForExitAsync(token); }
        catch
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stderr, stdout);
            throw;
        }
        await Task.WhenAll(stderr, stdout);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{name} 执行失败（退出码 {process.ExitCode}）：{(await stderr).Text}");
        var result = await stdout;
        if (result.Truncated) throw new InvalidDataException($"{name} 的输出信息过多。");
        return result.Text;
    }

    private static async Task<(string Text, bool Truncated)> ReadAsync(StreamReader reader, int limit, bool tail)
    {
        var text = new StringBuilder();
        var buffer = new char[2048];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            if (tail)
            {
                text.Append(buffer, 0, count);
                if (text.Length > limit) { text.Remove(0, text.Length - limit); truncated = true; }
            }
            else
            {
                var remaining = limit - text.Length;
                text.Append(buffer, 0, Math.Min(count, remaining));
                truncated |= count > remaining;
            }
        }
        return (text.ToString().Trim(), truncated);
    }
}
