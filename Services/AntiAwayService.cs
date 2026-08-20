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
        if (!GameWindowService.TryFindGameWindowHandle(out IntPtr hWnd, out _, out string? errorMessage))
        {
            return $"❌ {errorMessage ?? "Окно игры Neverwinter Online не найдено."}";
        }

        if (!await SendReturnKeyPressAsync(hWnd))
        {
            return "❌ Окно игры найдено, но не удалось отправить первое нажатие Enter.";
        }

        await Task.Delay(300);

        if (!await SendReturnKeyPressAsync(hWnd))
        {
            return "❌ Окно игры найдено, но не удалось отправить второе нажатие Enter.";
        }

        return "✅ Успешно разбудили игру.";
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
