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

    internal async Task ClearAsync()
    {
        // Cleanup must still run after task cancellation or plugin unload.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (_reactionApi is { } api)
                await api.SendMessageReactionAsync(message.Channel.Id, message.MessageId, FaceId, isAdd: false, cancellationToken: timeout.Token);
            if (_text is { } text)
                await context.Message.DeleteMessageAsync(text, timeout.Token);
        }
        catch (Exception error) { BotLog.Warning($"VideoTool 无法清理处理中提示：{error.Message}"); }
    }
}
