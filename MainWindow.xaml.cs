using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Globalization;
using NclaChatViewer.Models;
using NclaChatViewer.ViewModels;
using NclaChatViewer.Views;

namespace NclaChatViewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();

        viewModel = new MainViewModel();
        DataContext = viewModel;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Dispose();
        base.OnClosed(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void LogDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        LogDatePopup.IsOpen = false;
        LogDateDropDownButton.IsChecked = false;
    }

    private void PlayerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox comboBox || comboBox.SelectedItem is not string selectedPlayer)
        {
            return;
        }

        comboBox.Text = selectedPlayer;

        if (DataContext is MainViewModel model)
        {
            model.SelectedPlayer = selectedPlayer;
        }
    }

    private void ChatDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        DataGridRow? row = FindVisualParent<DataGridRow>(source);
        if (row is null)
        {
            dataGrid.SelectedItem = null;
            return;
        }

        row.IsSelected = true;
        dataGrid.SelectedItem = row.Item;
    }

    private void ChatDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ChatMessage message } dataGrid)
        {
            UpdatePrivateDialogMenuItem(dataGrid, message);
            return;
        }

        e.Handled = true;
    }

    private void CopyMessageTextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid dataGrid } }
            || dataGrid.SelectedItem is not ChatMessage message)
        {
            return;
        }

        System.Windows.Clipboard.SetText(message.Message ?? string.Empty);
    }

    private void ShowPrivateDialogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid dataGrid } }
            || dataGrid.SelectedItem is not ChatMessage selectedMessage)
        {
            return;
        }

        if (!TryGetPrivateDialogParticipants(selectedMessage, out string leftParticipant, out string rightParticipant))
        {
            return;
        }

        List<ChatMessage> messages = dataGrid.Items
            .OfType<ChatMessage>()
            .Where(message => IsPrivateDialogMessage(message, leftParticipant, rightParticipant))
            .OrderBy(message => message.Time)
            .ThenBy(message => message.Index)
            .ToList();

        if (messages.Count == 0)
        {
            return;
        }

        var dialog = new PrivateDialogWindow(leftParticipant, rightParticipant, messages)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private static void UpdatePrivateDialogMenuItem(DataGrid dataGrid, ChatMessage message)
    {
        MenuItem? showDialogItem = dataGrid.ContextMenu?.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "ShowPrivateDialog", StringComparison.Ordinal));

        if (showDialogItem is null)
        {
            return;
        }

        bool hasParticipants = TryGetPrivateDialogParticipants(message, out string leftParticipant, out _);
        bool canShowDialog = dataGrid.DataContext is ChatTabViewModel { IsPrivateTab: true }
            && hasParticipants;

        showDialogItem.Visibility = canShowDialog ? Visibility.Visible : Visibility.Collapsed;
        showDialogItem.IsEnabled = canShowDialog;

        if (canShowDialog)
        {
            showDialogItem.Header = $"Показать диалог с {leftParticipant}";
        }
    }

    private static bool TryGetPrivateDialogParticipants(ChatMessage message, out string leftParticipant, out string rightParticipant)
    {
        leftParticipant = NormalizePrivateDialogName(message.DisplayTarget);
        rightParticipant = NormalizePrivateDialogName(message.Player);

        return !string.IsNullOrWhiteSpace(leftParticipant)
            && !string.IsNullOrWhiteSpace(rightParticipant)
            && !string.Equals(leftParticipant, rightParticipant, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateDialogMessage(ChatMessage message, string leftParticipant, string rightParticipant)
    {
        string from = NormalizePrivateDialogName(message.Player);
        string to = NormalizePrivateDialogName(message.DisplayTarget);

        return (string.Equals(from, leftParticipant, StringComparison.OrdinalIgnoreCase)
                && string.Equals(to, rightParticipant, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(from, rightParticipant, StringComparison.OrdinalIgnoreCase)
                && string.Equals(to, leftParticipant, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePrivateDialogName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "@", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}


public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(object),
        typeof(BindingProxy),
        new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}


public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool boolValue && boolValue
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}


public sealed class LogDateAvailableConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DateTime date || values[1] is not IEnumerable dates)
        {
            return false;
        }

        foreach (object? item in dates)
        {
            if (item is DateTime availableDate && availableDate.Date == date.Date)
            {
                return true;
            }
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
