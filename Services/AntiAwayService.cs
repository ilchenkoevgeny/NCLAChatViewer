using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NclaChatViewer.Models;

namespace NclaChatViewer.Services;

public sealed class AntiAwayService
{
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
        var proc = Process.GetProcessesByName("GameClient").FirstOrDefault();

        if (proc == null)
        {
            return "❌ Окно игры Neverwinter Online не найдено.";
        }

        IntPtr hWnd = proc.MainWindowHandle;

        if (hWnd == IntPtr.Zero)
        {
            return "❌ Окно игры не найдено.";
        }

        // Первое нажатие с удержанием
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
        await Task.Delay(300);
        PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
        await Task.Delay(300);

        // Второе нажатие с удержанием
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
        await Task.Delay(300);
        PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);

        return "✅ Успешно разбудили игру.";
    }

    private static string NormalizeMessageText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalized = text.Replace('\u00A0', ' ').Trim();
        return WhitespaceRegex.Replace(normalized, " ");
    }
}