using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Media = System.Windows.Media;
using NclaChatViewer.Models;
using NclaChatViewer.Services;
using NclaChatViewer.ViewModels;

namespace NclaChatViewer.Views;

public partial class PrivateDialogWindow : Window
{
    public PrivateDialogWindow(string leftParticipant, string rightParticipant, IReadOnlyList<ChatMessage> messages)
    {
        InitializeComponent();
        DataContext = new PrivateDialogViewModel(leftParticipant, rightParticipant, messages);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (MessagesList.Items.Count > 0)
        {
            MessagesList.ScrollIntoView(MessagesList.Items[MessagesList.Items.Count - 1]);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
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

public sealed class PrivateDialogViewModel : ViewModelBase
{
    private readonly SettingsService settingsService = new();
    private readonly AppSettings settings;
    private string messageColorInput = string.Empty;
    private string senderColorInput = string.Empty;

    public PrivateDialogViewModel(string leftParticipant, string rightParticipant, IReadOnlyList<ChatMessage> messages)
    {
        settings = settingsService.Load();
        EnsureDialogSettingsDefaults();
        messageColorInput = settings.PrivateDialogMessageColor;
        senderColorInput = settings.PrivateDialogSenderColor;

        LeftParticipant = leftParticipant;
        RightParticipant = rightParticipant;
        Title = $"Диалог с {leftParticipant}";
        Messages = new ObservableCollection<PrivateDialogMessageViewModel>(
            messages.Select(message => new PrivateDialogMessageViewModel(message, leftParticipant, rightParticipant)));

        foreach (PrivateDialogMessageViewModel message in Messages)
        {
            message.ShowShortPlayerName = ShowShortPlayerName;
        }

        Subtitle = $"Сообщений: {Messages.Count:N0}";
        RangeText = BuildRangeText(messages);

        AvailableFontFamilies = new ObservableCollection<string>(
            Media.Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase));
        AvailableFontSizes = new ObservableCollection<double> { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24 };
        FontAppearanceOptions = new ObservableCollection<FontAppearanceOption>
        {
            new("Normal", "Обычный"),
            new("SemiBold", "Полужирный"),
            new("Bold", "Жирный"),
            new("Italic", "Курсив"),
            new("BoldItalic", "Жирный курсив")
        };
        ColorOptions = new ObservableCollection<string>
        {
            "#F05C77",
            "#EC5F6B",
            "#E95949",
            "#F36D59",
            "#F47C71",
            "#FF4D85",
            "#FEB3C6",
            "#FFE6ED",
            "#FF751A",
            "#FF9233",
            "#FFA366",
            "#FFC9B3",
            "#F4C63E",
            "#F2D026",
            "#F8CC3A",
            "#FFD166",
            "#FFE433",
            "#FFE666",
            "#FFFFE3",
            "#E9ECAC",
            "#EAF3A5",
            "#BADC6F",
            "#B5CE64",
            "#BEDB3D",
            "#BBC71F",
            "#CCDD55",
            "#B6D874",
            "#91C76B",
            "#C9EE91",
            "#A5D9A5",
            "#87C4A5",
            "#6BAE9C",
            "#24A894",
            "#219175",
            "#09C3A1",
            "#00E6E6",
            "#2DCDD2",
            "#00BBCC",
            "#00BBE6",
            "#46A0A0",
            "#006280",
            "#116DA2",
            "#439BD6",
            "#52A7E0",
            "#6DB9F8",
            "#BEE3F3",
            "#B6BAC8",
            "#F2F2F2",
            "#E6E6E6",
            "#E9E1C8",
            "#E0D4B8",
            "#D3DCDE",
            "#DBDBD6",
            "#927554",
            "#6F7675",
            "#595959",
            "#364D63"
        };
    }

    public string LeftParticipant { get; }

    public string RightParticipant { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string RangeText { get; }

    public ObservableCollection<PrivateDialogMessageViewModel> Messages { get; }

    public ObservableCollection<string> AvailableFontFamilies { get; }

    public ObservableCollection<double> AvailableFontSizes { get; }

    public ObservableCollection<FontAppearanceOption> FontAppearanceOptions { get; }

    public ObservableCollection<string> ColorOptions { get; }

    public string MessageFontFamily
    {
        get => settings.PrivateDialogMessageFontFamily;
        set
        {
            string safeValue = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;
            if (settings.PrivateDialogMessageFontFamily != safeValue)
            {
                settings.PrivateDialogMessageFontFamily = safeValue;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    public double MessageFontSize
    {
        get => settings.PrivateDialogMessageFontSize;
        set
        {
            double safeValue = Math.Clamp(value, 8, 40);
            if (Math.Abs(settings.PrivateDialogMessageFontSize - safeValue) > 0.01)
            {
                settings.PrivateDialogMessageFontSize = safeValue;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    public string MessageFontAppearance
    {
        get => settings.PrivateDialogMessageFontAppearance;
        set
        {
            string safeValue = NormalizeFontAppearance(value);
            if (settings.PrivateDialogMessageFontAppearance != safeValue)
            {
                settings.PrivateDialogMessageFontAppearance = safeValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MessageFontWeightValue));
                OnPropertyChanged(nameof(MessageFontStyleValue));
                SaveSettings();
            }
        }
    }

    public FontWeight MessageFontWeightValue => ToFontWeight(MessageFontAppearance);

    public System.Windows.FontStyle MessageFontStyleValue => ToFontStyle(MessageFontAppearance);

    public string MessageColor
    {
        get => messageColorInput;
        set
        {
            string input = value ?? string.Empty;
            if (!SetProperty(ref messageColorInput, input))
            {
                return;
            }

            if (TryNormalizeColor(input, out string normalizedColor))
            {
                messageColorInput = normalizedColor;
                settings.PrivateDialogMessageColor = normalizedColor;
                OnPropertyChanged();
                SaveSettings();
            }

            OnPropertyChanged(nameof(MessageColorBrush));
        }
    }

    public Media.Brush MessageColorBrush => BuildBrush(MessageColor, settings.PrivateDialogMessageColor);

    public string SenderFontFamily
    {
        get => settings.PrivateDialogSenderFontFamily;
        set
        {
            string safeValue = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;
            if (settings.PrivateDialogSenderFontFamily != safeValue)
            {
                settings.PrivateDialogSenderFontFamily = safeValue;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    public double SenderFontSize
    {
        get => settings.PrivateDialogSenderFontSize;
        set
        {
            double safeValue = Math.Clamp(value, 8, 32);
            if (Math.Abs(settings.PrivateDialogSenderFontSize - safeValue) > 0.01)
            {
                settings.PrivateDialogSenderFontSize = safeValue;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    public string SenderFontAppearance
    {
        get => settings.PrivateDialogSenderFontAppearance;
        set
        {
            string safeValue = NormalizeFontAppearance(value);
            if (settings.PrivateDialogSenderFontAppearance != safeValue)
            {
                settings.PrivateDialogSenderFontAppearance = safeValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SenderFontWeightValue));
                OnPropertyChanged(nameof(SenderFontStyleValue));
                SaveSettings();
            }
        }
    }

    public FontWeight SenderFontWeightValue => ToFontWeight(SenderFontAppearance);

    public System.Windows.FontStyle SenderFontStyleValue => ToFontStyle(SenderFontAppearance);

    public string SenderColor
    {
        get => senderColorInput;
        set
        {
            string input = value ?? string.Empty;
            if (!SetProperty(ref senderColorInput, input))
            {
                return;
            }

            if (TryNormalizeColor(input, out string normalizedColor))
            {
                senderColorInput = normalizedColor;
                settings.PrivateDialogSenderColor = normalizedColor;
                OnPropertyChanged();
                SaveSettings();
            }

            OnPropertyChanged(nameof(SenderColorBrush));
        }
    }

    public Media.Brush SenderColorBrush => BuildBrush(SenderColor, settings.PrivateDialogSenderColor);

    public bool ShowMessageTime
    {
        get => settings.PrivateDialogShowMessageTime;
        set
        {
            if (settings.PrivateDialogShowMessageTime != value)
            {
                settings.PrivateDialogShowMessageTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimeVisibility));
                SaveSettings();
            }
        }
    }

    public Visibility TimeVisibility => ShowMessageTime ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowShortPlayerName
    {
        get => settings.PrivateDialogShowShortPlayerName;
        set
        {
            if (settings.PrivateDialogShowShortPlayerName != value)
            {
                settings.PrivateDialogShowShortPlayerName = value;
                OnPropertyChanged();

                foreach (PrivateDialogMessageViewModel message in Messages)
                {
                    message.ShowShortPlayerName = value;
                }

                SaveSettings();
            }
        }
    }

    private static string BuildRangeText(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
        {
            return "Нет сообщений";
        }

        DateTime first = messages[0].Time;
        DateTime last = messages[^1].Time;

        return first.Date == last.Date
            ? $"Период: {first:dd.MM.yyyy}, {first:HH:mm:ss} - {last:HH:mm:ss}"
            : $"Период: {first:dd.MM.yyyy HH:mm:ss} - {last:dd.MM.yyyy HH:mm:ss}";
    }

    private void EnsureDialogSettingsDefaults()
    {
        if (string.IsNullOrWhiteSpace(settings.PrivateDialogMessageFontFamily))
        {
            settings.PrivateDialogMessageFontFamily = "Segoe UI";
        }

        if (settings.PrivateDialogMessageFontSize <= 0)
        {
            settings.PrivateDialogMessageFontSize = 13;
        }

        settings.PrivateDialogMessageFontAppearance = NormalizeFontAppearance(settings.PrivateDialogMessageFontAppearance);

        if (string.IsNullOrWhiteSpace(settings.PrivateDialogSenderFontFamily))
        {
            settings.PrivateDialogSenderFontFamily = "Segoe UI";
        }

        if (settings.PrivateDialogSenderFontSize <= 0)
        {
            settings.PrivateDialogSenderFontSize = 11;
        }

        settings.PrivateDialogSenderFontAppearance = NormalizeFontAppearance(settings.PrivateDialogSenderFontAppearance);

        if (!TryNormalizeColor(settings.PrivateDialogMessageColor, out string normalizedMessageColor))
        {
            settings.PrivateDialogMessageColor = GetDefaultMessageColor();
        }
        else
        {
            settings.PrivateDialogMessageColor = normalizedMessageColor;
        }

        if (!TryNormalizeColor(settings.PrivateDialogSenderColor, out string normalizedSenderColor))
        {
            settings.PrivateDialogSenderColor = GetDefaultSenderColor();
        }
        else
        {
            settings.PrivateDialogSenderColor = normalizedSenderColor;
        }
    }

    private void SaveSettings()
    {
        settingsService.Save(settings);
    }

    private string GetDefaultMessageColor()
    {
        return string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "#111827"
            : "#F8FAFC";
    }

    private string GetDefaultSenderColor()
    {
        return string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "#52627A"
            : "#A8B3C7";
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        return TryNormalizeColor(value, out string normalizedColor)
            ? normalizedColor
            : fallback;
    }

    private static bool TryNormalizeColor(string? value, out string normalizedColor)
    {
        normalizedColor = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string color = value.Trim();
        if (!color.StartsWith('#'))
        {
            color = $"#{color}";
        }

        try
        {
            _ = (Media.Color)Media.ColorConverter.ConvertFromString(color)!;
            normalizedColor = color.ToUpperInvariant();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Media.Brush BuildBrush(string value, string fallback)
    {
        string color = NormalizeColor(value, fallback);
        return new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(color)!);
    }

    private static string NormalizeFontAppearance(string? value)
    {
        return value switch
        {
            "SemiBold" => "SemiBold",
            "Bold" => "Bold",
            "Italic" => "Italic",
            "BoldItalic" => "BoldItalic",
            _ => "Normal"
        };
    }

    private static FontWeight ToFontWeight(string value)
    {
        return value switch
        {
            "SemiBold" => FontWeights.SemiBold,
            "Bold" or "BoldItalic" => FontWeights.Bold,
            _ => FontWeights.Normal
        };
    }

    private static System.Windows.FontStyle ToFontStyle(string value)
    {
        return value is "Italic" or "BoldItalic"
            ? FontStyles.Italic
            : FontStyles.Normal;
    }
}

public sealed class FontAppearanceOption
{
    public FontAppearanceOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }

    public string DisplayName { get; }
}

public sealed class PrivateDialogMessageViewModel : ViewModelBase
{
    private bool showShortPlayerName;

    public PrivateDialogMessageViewModel(ChatMessage message, string leftParticipant, string rightParticipant)
    {
        From = string.IsNullOrWhiteSpace(message.Player) ? "@" : message.Player;
        To = string.IsNullOrWhiteSpace(message.DisplayTarget) ? string.Empty : message.DisplayTarget;
        Message = message.Message;
        TimeText = message.Time.ToString("dd.MM.yyyy HH:mm:ss");
        IsOutgoing = string.Equals(NormalizeName(message.Player), rightParticipant, StringComparison.OrdinalIgnoreCase);
    }

    public string From { get; }

    public string To { get; }

    public string Header => ShowShortPlayerName ? StripDescriptor(From) : From;

    public string Message { get; }

    public string TimeText { get; }

    public bool IsOutgoing { get; }

    public bool ShowShortPlayerName
    {
        get => showShortPlayerName;
        set
        {
            if (SetProperty(ref showShortPlayerName, value))
            {
                OnPropertyChanged(nameof(Header));
            }
        }
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "@", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static string StripDescriptor(string value)
    {
        int atIndex = value.IndexOf('@');
        return atIndex > 0 ? value[..atIndex] : value;
    }
}
