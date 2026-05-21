using NclaChatViewer.ViewModels;

namespace NclaChatViewer.Models;

public sealed class NotificationRule : ViewModelBase
{
    private bool isEnabled = true;
    private string triggerKind = NotificationTriggerKinds.TextContains;
    private string chatFilter = "Все чаты";
    private string phrase = string.Empty;
    private bool playSound = true;
    private bool showPopup = true;
    private bool disappearingPopup;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public string TriggerKind
    {
        get => triggerKind;
        set
        {
            string normalizedValue = string.IsNullOrWhiteSpace(value)
                ? NotificationTriggerKinds.TextContains
                : value;

            if (SetProperty(ref triggerKind, normalizedValue))
            {
                OnPropertyChanged(nameof(IsTextContains));
                OnPropertyChanged(nameof(IsPrivateMessage));
                OnPropertyChanged(nameof(IsPlayerDescriptor));
            }
        }
    }

    public bool IsTextContains => string.Equals(
        TriggerKind,
        NotificationTriggerKinds.TextContains,
        StringComparison.OrdinalIgnoreCase);

    public bool IsPrivateMessage => string.Equals(
        TriggerKind,
        NotificationTriggerKinds.PrivateMessage,
        StringComparison.OrdinalIgnoreCase);

    public bool IsPlayerDescriptor => string.Equals(
        TriggerKind,
        NotificationTriggerKinds.PlayerDescriptor,
        StringComparison.OrdinalIgnoreCase);

    public string ChatFilter
    {
        get => chatFilter;
        set => SetProperty(ref chatFilter, string.IsNullOrWhiteSpace(value) ? "Все чаты" : value);
    }

    public string Phrase
    {
        get => phrase;
        set => SetProperty(ref phrase, value ?? string.Empty);
    }

    public bool PlaySound
    {
        get => playSound;
        set => SetProperty(ref playSound, value);
    }

    public bool ShowPopup
    {
        get => showPopup;
        set
        {
            if (SetProperty(ref showPopup, value))
            {
                OnPropertyChanged(nameof(CanUseDisappearingPopup));

                if (!value)
                {
                    DisappearingPopup = false;
                }
            }
        }
    }

    public bool DisappearingPopup
    {
        get => disappearingPopup;
        set => SetProperty(ref disappearingPopup, ShowPopup && value);
    }

    public bool CanUseDisappearingPopup => ShowPopup;
}

public static class NotificationTriggerKinds
{
    public const string PrivateMessage = "Личное сообщение";
    public const string TextContains = "Текст в сообщении";
    public const string PlayerDescriptor = "Дескриптор игрока";
}
