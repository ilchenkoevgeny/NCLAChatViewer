using NclaChatViewer.Services;

namespace NclaChatViewer.Models;

public sealed class ChatMessage
{
    public long Index { get; init; }

    public DateTime Time { get; init; }

    public int Unknown { get; init; }

    public string Player { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string ChannelName1 { get; init; } = string.Empty;

    public string ChannelName2 { get; init; } = string.Empty;

    public string ChatType { get; init; } = string.Empty;

    public string DisplayChatType => ChatTypeDisplayService.GetDisplayName(ChatType);

    public bool IsPrivateMessage => string.Equals(
        ChatTypeDisplayService.GetTabGroupName(ChatType),
        "Private",
        StringComparison.OrdinalIgnoreCase);

    public string ChatName => string.IsNullOrWhiteSpace(ChannelName1) || ChannelName1 == "@"
        ? ChannelName2
        : ChannelName1;

    /// <summary>
    /// Адресат сообщения. Для личных исходящих сообщений Neverwinter пишет получателя в поле Target.
    /// Для обычных чатов поле обычно содержит "@", поэтому в таблице показываем пустое значение.
    /// </summary>
    public string DisplayTarget => Target == "@" ? string.Empty : Target;

    /// <summary>
    /// Краткая подсказка по направлению личного сообщения.
    /// Нужна для вкладки личных сообщений, где Private и Private_Sent объединены в одну хронологическую переписку.
    /// </summary>
    public string PrivateDirection
    {
        get
        {
            if (!IsPrivateMessage)
            {
                return string.Empty;
            }

            if (string.Equals(ChatType, "Private_Sent", StringComparison.OrdinalIgnoreCase))
            {
                return "Исходящее";
            }

            if (string.Equals(ChatType, "Private_Received", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ChatType, "Private", StringComparison.OrdinalIgnoreCase))
            {
                return "Входящее";
            }

            return string.Empty;
        }
    }

    public string Message { get; init; } = string.Empty;

    public string RawLine { get; init; } = string.Empty;
}
