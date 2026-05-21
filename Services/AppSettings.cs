using NclaChatViewer.Models;

namespace NclaChatViewer.Services;

public sealed class AppSettings
{
    public string LogsDirectory { get; set; } = "Samples";

    public string FileNamePattern { get; set; } = "chat_{0:yyyy-MM-dd}_*.log";

    public int RefreshIntervalMs { get; set; } = 2000;

    public bool OpenTodayFileOnStart { get; set; } = true;

    public bool ReadExistingFileOnOpen { get; set; } = true;

    public bool IgnoreCombatChats { get; set; } = true;

    public bool AutoSwitchToLatestTodayFile { get; set; } = true;

    public bool KeepMessagesWhenLogFileChanges { get; set; } = true;

    public bool ClearMessagesWhenDateChanges { get; set; } = true;

    public string Theme { get; set; } = "Dark";

    public string TableFontFamily { get; set; } = "Segoe UI";

    public double TableFontSize { get; set; } = 13;

    public string TableFontWeight { get; set; } = "Normal";

    public string TableFontStyle { get; set; } = "Normal";

    public bool KeepGameActive { get; set; }

    public bool AntiAway { get; set; }

    public int KeepGameActiveIntervalMinutes { get; set; } = 10;

    public bool NotificationsEnabled { get; set; }

    public bool UseSystemNotificationSound { get; set; } = true;

    public string NotificationSoundFilePath { get; set; } = string.Empty;

    public List<NotificationRule> NotificationRules { get; set; } = new();
}

