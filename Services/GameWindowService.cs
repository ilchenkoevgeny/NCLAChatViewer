using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NclaChatViewer.Services;

public sealed class GameWindowService
{
    private const int SW_RESTORE = 9;

    public bool TryActivateGameWindow(out string status)
    {
        Process? process = Process.GetProcessesByName("GameClient")
            .FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);

        if (process is null)
        {
            status = "Процесс GameClient.exe или окно игры не найдено.";
            return false;
        }

        IntPtr windowHandle = process.MainWindowHandle;
        if (windowHandle == IntPtr.Zero)
        {
            status = "Окно игры не найдено.";
            return false;
        }

        _ = ShowWindow(windowHandle, SW_RESTORE);
        bool activated = SetForegroundWindow(windowHandle);

        status = activated
            ? "Окно игры развернуто."
            : "Не удалось вывести окно игры на передний план.";

        return activated;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
