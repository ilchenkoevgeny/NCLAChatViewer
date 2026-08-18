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
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly Action openGameAction;
    private readonly bool disappearingPopup;
    private DispatcherTimer? closeTimer;

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
        MakeWindowNonActivating();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 24;
        Top = workArea.Bottom - ActualHeight - 24;

        if (disappearingPopup)
        {
            StartAutoCloseTimer();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        closeTimer?.Stop();
        closeTimer = null;
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

        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
