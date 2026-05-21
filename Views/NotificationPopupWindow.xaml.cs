using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NclaChatViewer.Models;

namespace NclaChatViewer.Views;

public partial class NotificationPopupWindow : Window
{
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

    private void OpenGameButton_Click(object sender, RoutedEventArgs e)
    {
        openGameAction();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
