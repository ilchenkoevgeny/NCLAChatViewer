using System.Windows;
using System.Windows.Input;

namespace NclaChatViewer.Views;

public partial class ImportLogsDialogWindow : Window
{
    public ImportLogsDialogWindow(
        string title,
        string message,
        string primaryText,
        string secondaryText,
        bool showSecondaryButton = true,
        string iconText = "?")
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        SecondaryButton.Content = secondaryText;
        SecondaryButton.Visibility = showSecondaryButton ? Visibility.Visible : Visibility.Collapsed;
        IconText.Text = iconText;
    }

    public static bool ConfirmImport(Window? owner, int filesCount)
    {
        string message = filesCount == 1
            ? "Найден файл чата, который еще не импортирован в базу данных. Импортировать его сейчас?"
            : $"Найдены файлы чатов, которые еще не импортированы в базу данных: {filesCount}. Импортировать их сейчас?";

        var dialog = new ImportLogsDialogWindow(
            "Импорт старых чатов",
            message,
            "Импортировать",
            "Позже");

        PrepareOwner(dialog, owner);

        return dialog.ShowDialog() == true;
    }

    public static void ShowNotice(Window? owner, string title, string message)
    {
        var dialog = new ImportLogsDialogWindow(
            title,
            message,
            "ОК",
            string.Empty,
            showSecondaryButton: false,
            iconText: "!");

        PrepareOwner(dialog, owner);

        dialog.ShowDialog();
    }

    private static void PrepareOwner(Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
