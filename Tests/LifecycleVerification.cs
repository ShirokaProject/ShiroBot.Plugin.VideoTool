using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ShiroBot.Plugin.VideoTool;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

internal static class LifecycleVerification
{
    internal static async Task RunAsync(string root, string input, string ffmpeg, string gifsicle, CancellationToken token)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var listener = new HttpListener();
        var url = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(url);
        listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var serving = ServeAsync(listener, input, stop.Token);
        try
        {
            var references = new List<WeakReference>();
            var initialProcesses = Processes();
            for (var round = 0; round < 6; round++)
            {
                token.ThrowIfCancellationRequested();
                references.Add(LoadConvertUnload(root, url, ffmpeg, gifsicle, round));
                // Let disposed process callbacks and completed async continuations retire.
                await Task.Delay(50, token);
                for (var collect = 0; collect < 5 && references.Any(r => r.IsAlive); collect++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(50, token);
                }
                if (references.Any(r => r.IsAlive)) throw new Exception("Plugin collectible context remained rooted after unload, round " + round);
                Console.WriteLine($"PASS Lifecycle round {round + 1}: real conversion, unload and collectible assembly reclaimed");
            }
            var remaining = Processes().Except(initialProcesses).ToArray();
            if (remaining.Length != 0) throw new Exception("Unexpected ffmpeg/ffprobe/gifsicle processes remain: " + string.Join(',', remaining));
            Console.WriteLine("PASS Repeated unload retains no plugin load contexts or child codec processes");
        }
        finally
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await serving; }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) { }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadConvertUnload(string root, string url, string ffmpeg, string gifsicle, int round)
    {
        var context = new PluginLoadContext();
        var assembly = context.LoadFromAssemblyPath(typeof(VideoToolPlugin).Assembly.Location);
        var plugin = (IBotPlugin)Activator.CreateInstance(assembly.GetType("ShiroBot.Plugin.VideoTool.VideoToolPlugin", throwOnError: true)!)!;
        var images = 0;
        var messages = StubProxy.Make<IMessageContext>((method, args) => method.Name switch
        {
            "GetMessageAsync" => Task.FromResult<MessageEvent?>(new MessageEvent
            {
                Platform = "test", InstanceId = "lifecycle", MessageId = "source", Channel = Channel.Direct("user"),
                Sender = new User("user"), Segments = [new VideoSegment(url)]
            }),
            "ReplyAsync" or "QuoteReplyAsync" => Reply(args!),
            _ => throw new NotSupportedException(method.Name)
        });
        var config = StubProxy.Make<IConfigContext>((method, _) =>
        {
            if (method.Name != "Load") throw new NotSupportedException(method.Name);
            var type = method.GetGenericArguments().Single();
            var value = Activator.CreateInstance(type)!;
            foreach (var (key, setting) in new (string, object)[]
            {
                ("FfmpegPath", ffmpeg), ("GifsiclePath", gifsicle), ("Width", 64), ("Height", 64),
                ("StartSeconds", 0d), ("DurationSeconds", 0.25d)
            }) type.GetProperty(key)!.SetValue(value, setting);
            return value;
        });
        var host = StubProxy.Make<IBotContext>((method, args) => method.Name switch
        {
            "get_Config" => config,
            "get_Message" => messages,
            "CreateTempDirectory" => Temp((TimeSpan)args![0]!),
            _ => throw new NotSupportedException(method.Name)
        });
        plugin.OnLoad(host).GetAwaiter().GetResult();
        try
        {
            ((IBotEventSubscriber)plugin).OnEventAsync(new MessageEvent
            {
                Platform = "test", InstanceId = "lifecycle", MessageId = "request", Channel = Channel.Direct("user"),
                Sender = new User("user"), Segments = [new QuoteSegment("source"), new TextSegment("2gif")]
            }).GetAwaiter().GetResult();
            if (images != 1) throw new Exception("Lifecycle conversion did not send a GIF");
            // Exercise the static probe cache as well as HTTP and Process resources.
            if (round == 0)
            {
                var result = ((IPluginActionProvider)plugin).ExecuteActionAsync("probe-encoders").GetAwaiter().GetResult();
                if (!result.Ok) throw new Exception(result.Message);
            }
        }
        finally { plugin.OnUnload().GetAwaiter().GetResult(); }
        var weak = new WeakReference(context);
        context.Unload();
        return weak;

        object Reply(object?[] args)
        {
            if (args[1] is IEnumerable<MessageSegment> segments) images += segments.OfType<ImageSegment>().Count();
            return Task.FromResult(new SentMessage("sent"));
        }
        TemporaryDirectory Temp(TimeSpan retention)
        {
            var directory = Path.Combine(root, "lifecycle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new(directory, DateTimeOffset.UtcNow + retention);
        }
    }

    private sealed class PluginLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
    }

    private static HashSet<int> Processes()
    {
        var ids = new HashSet<int>();
        foreach (var name in new[] { "ffmpeg", "ffprobe", "gifsicle" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process) ids.Add(process.Id);
        return ids;
    }

    private static async Task ServeAsync(HttpListener listener, string input, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var request = await listener.GetContextAsync().WaitAsync(token);
            try
            {
                await using var source = File.OpenRead(input);
                request.Response.ContentLength64 = source.Length;
                await source.CopyToAsync(request.Response.OutputStream, token);
            }
            finally { request.Response.Close(); }
        }
    }
}
