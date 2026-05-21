using System.Runtime.InteropServices;

namespace NclaChatViewer.Services;

public sealed class ActivityKeepAliveService
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public void KeepSystemAwakeOnce()
    {
        _ = SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }

    public void Reset()
    {
        _ = SetThreadExecutionState(ES_CONTINUOUS);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
