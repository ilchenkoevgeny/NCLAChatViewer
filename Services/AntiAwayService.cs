using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NclaChatViewer.Models;

namespace NclaChatViewer.Services;

public sealed class AntiAwayService
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const int VK_RETURN = 0x0D;

    private const string AwayKickWarningSender = "AwayKickWarning@";
    private const string AwayKickWarningChatType = "System";
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public bool IsAwayKickWarning(ChatMessage message)
    {
        if (message is null) return false;
        if (!string.Equals(message.Player, AwayKickWarningSender, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(message.ChatType, AwayKickWarningChatType, StringComparison.OrdinalIgnoreCase)) return false;
        string normalized = NormalizeMessageText(message.Message);
        return normalized.Contains("вы находитесь в состоянии бездействия", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("автоматический выход из системы", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> HandleAwayKickWarningAsync(ChatMessage message)
    {
        string? firstError = await SendReturnKeyPressToCurrentGameAsync();
        if (firstError is not null)
        {
            return $"❌ {firstError}";
        }

        await Task.Delay(300);

        string? secondError = await SendReturnKeyPressToCurrentGameAsync();
        if (secondError is not null)
        {
            return $"❌ {secondError}";
        }

        return "✅ Успешно разбудили игру.";
    }

    private static async Task<string?> SendReturnKeyPressToCurrentGameAsync()
    {
        if (!GameWindowService.TryFindGameWindowHandle(out IntPtr hWnd, out _, out string? errorMessage))
        {
            return errorMessage ?? "Окно игры Neverwinter Online не найдено.";
        }

        if (await SendReturnKeyPressAsync(hWnd))
        {
            return null;
        }

        // Между поиском окна и отправкой сообщения GameClient мог завершиться и запуститься заново.
        // Повторно ищем актуальное окно и пробуем ещё раз уже с новым HWND.
        if (GameWindowService.TryFindGameWindowHandle(out IntPtr retryHWnd, out _, out _)
            && retryHWnd != hWnd
            && await SendReturnKeyPressAsync(retryHWnd))
        {
            return null;
        }

        return "Окно игры найдено, но не удалось отправить нажатие Enter.";
    }

    private static async Task<bool> SendReturnKeyPressAsync(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        bool keyDownSent = PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
        await Task.Delay(300);
        bool keyUpSent = PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);

        return keyDownSent && keyUpSent;
    }

    private static string NormalizeMessageText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalized = text.Replace('\u00A0', ' ').Trim();
        return WhitespaceRegex.Replace(normalized, " ");
    }
}
