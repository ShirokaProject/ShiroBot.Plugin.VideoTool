using ShiroBot.Model.QQ;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugin.VideoTool;

internal sealed class ProcessingNotice(IBotContext context, MessageEvent message)
{
    private const string FaceId = "351";
    private IQGroupApi? _reactionApi;
    private MessageReference? _text;

    internal async Task ShowAsync(CancellationToken token)
    {
        if (message.Channel.Type == ChannelType.Group && !string.IsNullOrWhiteSpace(message.MessageId))
        {
            try
            {
                if (context.GetAdapterExtension<IQGroupApi>() is { } api)
                {
                    await api.SendMessageReactionAsync(message.Channel.Id, message.MessageId, FaceId, cancellationToken: token);
                    _reactionApi = api;
                    return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { BotLog.Info($"VideoTool 处理中表情不可用，改用文字：{error.Message}"); }
        }

        var sent = await context.Message.ReplyAsync(message, [new TextSegment("正在处理视频…")], token);
        if (sent.IsSuccess && !string.IsNullOrWhiteSpace(sent.MessageId))
            _text = sent.Reference ?? new MessageReference(message.InstanceId!, message.Channel, sent.MessageId);
    }

    internal async Task ClearAsync(bool succeeded)
    {
        // Cleanup must still run after task cancellation or plugin unload.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (_reactionApi is { } api)
        {
            await TryAsync(() => api.SendMessageReactionAsync(message.Channel.Id, message.MessageId, FaceId, isAdd: false, cancellationToken: timeout.Token));
            await TryAsync(() => api.SendMessageReactionAsync(message.Channel.Id, message.MessageId, succeeded ? "426" : "9", cancellationToken: timeout.Token));
        }
        if (_text is { } text)
            await TryAsync(() => context.Message.DeleteMessageAsync(text, timeout.Token));
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { BotLog.Warning($"VideoTool 无法更新处理状态：{error.Message}"); }
    }
}
