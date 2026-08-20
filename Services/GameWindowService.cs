using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NclaChatViewer.Services;

public sealed class GameWindowService
{
    private const int SW_RESTORE = 9;
    private const int MaxWindowTitleLength = 512;
    private const int MaxClassNameLength = 256;

    private const int GWL_STYLE = -16;
    private const int GA_ROOT = 2;
    private const int GW_OWNER = 4;

    private const long WS_DISABLED = 0x08000000L;

    private static readonly string[] GameProcessNames =
    {
        "GameClient"
    };

    private static readonly string[] GameWindowMarkers =
    {
        "Neverwinter",
        "GameClient"
    };

    public bool TryActivateGameWindow(out string status)
    {
        if (!TryFindGameWindowHandle(out IntPtr windowHandle, out _, out string? errorMessage))
        {
            status = errorMessage ?? "Окно игры Neverwinter Online не найдено.";
            return false;
        }

        _ = ShowWindow(windowHandle, SW_RESTORE);
        bool activated = SetForegroundWindow(windowHandle);

        status = activated
            ? "Окно игры развернуто."
            : "Не удалось вывести окно игры на передний план.";

        return activated;
    }

    internal static bool TryFindGameWindowHandle(out IntPtr windowHandle, out Process? process, out string? errorMessage)
    {
        windowHandle = IntPtr.Zero;
        process = null;
        errorMessage = null;

        List<Process> gameProcesses = GameProcessNames
            .SelectMany(Process.GetProcessesByName)
            .OrderByDescending(item => item.MainWindowHandle != IntPtr.Zero)
            .ThenBy(item => item.Id)
            .ToList();

        if (gameProcesses.Count == 0)
        {
            errorMessage = "Процесс GameClient.exe не найден.";
            return false;
        }

        foreach (Process gameProcess in gameProcesses)
        {
            if (TryGetBestWindowHandle(gameProcess, out IntPtr handle))
            {
                windowHandle = handle;
                process = gameProcess;
                return true;
            }
        }

        errorMessage = "Процесс GameClient.exe найден, но подходящее окно игры не найдено.";
        return false;
    }

    private static bool TryGetBestWindowHandle(Process process, out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;

        var candidates = new List<(IntPtr Handle, int Score)>();

        IntPtr mainWindowHandle = process.MainWindowHandle;
        if (IsUsableGameWindow(mainWindowHandle, process.Id))
        {
            candidates.Add((mainWindowHandle, GetWindowScore(mainWindowHandle, mainWindowHandle)));
        }

        foreach (IntPtr handle in EnumerateProcessWindows(process.Id))
        {
            if (!IsUsableGameWindow(handle, process.Id))
            {
                continue;
            }

            candidates.Add((handle, GetWindowScore(handle, mainWindowHandle)));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        windowHandle = candidates
            .GroupBy(candidate => candidate.Handle)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .First()
            .Handle;

        return windowHandle != IntPtr.Zero;
    }

    private static IReadOnlyList<IntPtr> EnumerateProcessWindows(int processId)
    {
        var handles = new List<IntPtr>();

        _ = EnumWindows((handle, _) =>
        {
            _ = GetWindowThreadProcessId(handle, out int windowProcessId);
            if (windowProcessId == processId)
            {
                handles.Add(handle);
            }

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    private static bool IsUsableGameWindow(IntPtr handle, int processId)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(handle, out int windowProcessId);
        if (windowProcessId != processId)
        {
            return false;
        }

        if (GetAncestor(handle, GA_ROOT) != handle)
        {
            return false;
        }

        if (GetWindow(handle, GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        if (!IsWindowVisible(handle))
        {
            return false;
        }

        long style = GetWindowLongPtr(handle, GWL_STYLE).ToInt64();
        return (style & WS_DISABLED) == 0;
    }

    private static int GetWindowScore(IntPtr handle, IntPtr mainWindowHandle)
    {
        int score = 0;

        if (handle == mainWindowHandle)
        {
            score += 100;
        }

        string title = GetWindowTitle(handle);
        if (!string.IsNullOrWhiteSpace(title))
        {
            score += 30;
        }

        string className = GetWindowClassName(handle);
        if (!string.IsNullOrWhiteSpace(className))
        {
            score += 10;
        }

        if (GameWindowMarkers.Any(marker => title.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            score += 100;
        }

        if (GameWindowMarkers.Any(marker => className.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            score += 60;
        }

        return score;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var builder = new StringBuilder(MaxWindowTitleLength);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowClassName(IntPtr handle)
    {
        var builder = new StringBuilder(MaxClassNameLength);
        _ = GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, int gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLongPtr32(hWnd, nIndex);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
