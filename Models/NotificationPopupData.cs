namespace NclaChatViewer.Models;

public sealed class NotificationPopupData
{
    public string Title { get; init; } = "Уведомление";

    public string Time { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public string Chat { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static NotificationPopupData FromMessage(ChatMessage message)
    {
        string title = message.IsPrivateMessage
            ? "Личное сообщение"
            : "Сработало уведомление";

        return new NotificationPopupData
        {
            Title = title,
            Time = message.Time.ToString("HH:mm:ss"),
            From = string.IsNullOrWhiteSpace(message.Player) ? "—" : message.Player,
            To = string.IsNullOrWhiteSpace(message.DisplayTarget) ? "—" : message.DisplayTarget,
            Chat = string.IsNullOrWhiteSpace(message.ChatName) ? "—" : message.ChatName,
            Type = string.IsNullOrWhiteSpace(message.DisplayChatType) ? message.ChatType : message.DisplayChatType,
            Message = message.Message
        };
    }
}
