using System.Media;
using System.Runtime.InteropServices;
using NclaChatViewer.Models;

namespace NclaChatViewer.Services;

public sealed class NotificationService
{
    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool MessageBeep(uint uType);

    public bool ShouldNotify(ChatMessage message, NotificationRule rule)
    {
        if (!rule.IsEnabled)
        {
            return false;
        }

        if (IsOutgoingPrivateMessage(message))
        {
            return false;
        }

        if (string.Equals(rule.TriggerKind, NotificationTriggerKinds.PrivateMessage, StringComparison.OrdinalIgnoreCase))
        {
            return IsIncomingPrivateMessage(message);
        }

        if (string.Equals(rule.TriggerKind, NotificationTriggerKinds.PlayerDescriptor, StringComparison.OrdinalIgnoreCase))
        {
            return IsPlayerDescriptorMatched(message, rule.Phrase);
        }

        if (!IsChatMatched(message, rule.ChatFilter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Phrase))
        {
            return false;
        }

        return message.Message?.IndexOf(rule.Phrase, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public bool PlayNotificationSound(bool useSystemSound, string? customSoundFilePath, out string? errorMessage)
    {
        errorMessage = null;

        if (!useSystemSound)
        {
            if (string.IsNullOrWhiteSpace(customSoundFilePath))
            {
                errorMessage = "Файл звука уведомления не выбран.";
                return false;
            }

            if (!System.IO.File.Exists(customSoundFilePath))
            {
                errorMessage = $"Файл звука уведомления не найден: {customSoundFilePath}";
                return false;
            }

            try
            {
                using var player = new SoundPlayer(customSoundFilePath);
                player.Play();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Не удалось проиграть WAV-файл уведомления: {ex.Message}";
                return false;
            }
        }

        try
        {
            SystemSounds.Exclamation.Play();
            _ = MessageBeep(0xFFFFFFFF);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                _ = MessageBeep(0xFFFFFFFF);
                return true;
            }
            catch (Exception fallbackEx)
            {
                errorMessage = $"Не удалось проиграть системный звук уведомления: {ex.Message}; {fallbackEx.Message}";
                return false;
            }
        }
    }

    private static bool IsIncomingPrivateMessage(ChatMessage message)
    {
        return string.Equals(message.ChatType, "Private", StringComparison.OrdinalIgnoreCase)
               || string.Equals(message.ChatType, "Private_Received", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOutgoingPrivateMessage(ChatMessage message)
    {
        return string.Equals(message.ChatType, "Private_Sent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlayerDescriptorMatched(ChatMessage message, string? descriptorFilter)
    {
        string normalizedFilter = NormalizeDescriptor(descriptorFilter);
        if (string.IsNullOrWhiteSpace(normalizedFilter))
        {
            return false;
        }

        return string.Equals(ExtractPlayerDescriptor(message.Player), normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ExtractPlayerDescriptor(message.DisplayTarget), normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractPlayerDescriptor(string? player)
    {
        if (string.IsNullOrWhiteSpace(player))
        {
            return string.Empty;
        }

        int atIndex = player.IndexOf('@');
        return atIndex >= 0
            ? player[atIndex..].Trim()
            : string.Empty;
    }

    private static string NormalizeDescriptor(string? descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return string.Empty;
        }

        string value = descriptor.Trim();
        int atIndex = value.IndexOf('@');
        if (atIndex >= 0)
        {
            return value[atIndex..];
        }

        return value.StartsWith('#')
            ? string.Empty
            : "@" + value;
    }

    private static bool IsChatMatched(ChatMessage message, string? chatFilter)
    {
        if (string.IsNullOrWhiteSpace(chatFilter) || string.Equals(chatFilter, "Все чаты", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(message.ChatName, chatFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.ChatType, chatFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.DisplayChatType, chatFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string filterDisplayName = ChatTypeDisplayService.GetChannelDisplayName(chatFilter);
        string messageChatDisplayName = ChatTypeDisplayService.GetChannelDisplayName(message.ChatName);
        string messageTypeDisplayName = ChatTypeDisplayService.GetChannelDisplayName(message.ChatType);

        return string.Equals(messageChatDisplayName, filterDisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(messageTypeDisplayName, filterDisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.DisplayChatType, filterDisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
