using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NclaChatViewer.Models;

namespace NclaChatViewer.Views;

public partial class NotificationPopupWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_HWNDPARENT = -8;

    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    private readonly Action openGameAction;
    private readonly bool disappearingPopup;
    private DispatcherTimer? closeTimer;
    private HwndSource? hwndSource;

    public NotificationPopupWindow(
        NotificationPopupData data,
        Action openGameAction,
        bool disappearingPopup)
    {
        InitializeComponent();
        DataContext = data;
        this.openGameAction = openGameAction;
        this.disappearingPopup = disappearingPopup;

        Topmost = true;
        ShowActivated = false;
        Focusable = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        hwndSource?.AddHook(WindowMessageHook);

        MakeWindowNonActivating();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 24;
        Top = workArea.Bottom - ActualHeight - 24;

        ApplyTopmostWithoutActivation();

        if (disappearingPopup)
        {
            StartAutoCloseTimer();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        closeTimer?.Stop();
        closeTimer = null;

        hwndSource?.RemoveHook(WindowMessageHook);
        hwndSource = null;

        base.OnClosed(e);
    }

    private void StartAutoCloseTimer()
    {
        closeTimer?.Stop();
        closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer?.Stop();
            FadeOutAndClose();
        };
        closeTimer.Start();
    }

    private void FadeOutAndClose()
    {
        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(650),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    private void MakeWindowNonActivating()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr extendedStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        long updatedStyle = extendedStyle.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(updatedStyle));

        // Уведомление не должно быть owned-window главного окна.
        // Иначе Windows может поднять или активировать главное окно вместе с popup-окном.
        _ = SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, IntPtr.Zero);

        ApplyTopmostWithoutActivation();
    }

    private void ApplyTopmostWithoutActivation()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private void OpenGameButton_Click(object sender, RoutedEventArgs e)
    {
        openGameAction();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

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

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
