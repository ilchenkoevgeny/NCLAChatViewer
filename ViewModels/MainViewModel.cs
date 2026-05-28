using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Principal;
using System.Windows.Data;
using Media = System.Windows.Media;
using Threading = System.Windows.Threading;
using Win32 = Microsoft.Win32;
using Forms = System.Windows.Forms;
using NclaChatViewer.Models;
using NclaChatViewer.Services;
using NclaChatViewer.Views;

namespace NclaChatViewer.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private const string AllTabName = "All";
    private const string AllPlayersValue = "Все игроки";
    private const string AllChannelsValue = "Все чаты";

    private readonly SettingsService settingsService = new();
    private readonly ThemeService themeService = new();
    private readonly ChatLogTailReader tailReader = new();
    private readonly NotificationService notificationService = new();
    private readonly ActivityKeepAliveService activityKeepAliveService = new();
    private readonly AntiAwayService antiAwayService = new();
    private readonly GameWindowService gameWindowService = new();
    private readonly SemaphoreSlim readLock = new(1, 1);
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private readonly System.Threading.Timer timer;

    private AppSettings settings;
    private ChatDatabaseService chatDatabase = null!;
    private ICollectionView? visibleTabs;
    private ChatTabViewModel? selectedTab;
    private string? selectedPlayer = AllPlayersValue;
    private bool filterPlayerByDescriptor;
    private bool isSettingsExpanded;
    private bool isNotificationsExpanded;
    private string? selectedChannel = AllChannelsValue;
    private string searchText = string.Empty;
    private string statusText = "Файл не открыт";
    private string currentFilePath = string.Empty;
    private bool autoFollowTodayFile;
    private DateTime followedLogDate = DateTime.Today;
    private bool isLoading;
    private int loadingProgress;
    private string loadingTitle = string.Empty;
    private string loadingProgressText = "0%";
    private string loadingText = string.Empty;
    private DateTime? selectedLogDate = DateTime.Today;
    private bool searchWholeHistory;
    private CancellationTokenSource? databaseLoadCancellation;
    private bool suppressLogDateReload;
    private bool suppressFilterChange;
    private bool disposed;

    public MainViewModel()
    {
        settings = settingsService.Load();
        chatDatabase = new ChatDatabaseService(settings.DatabasePath, settingsService.SettingsDirectory);
        settings.DatabasePath = chatDatabase.DatabasePath;
        themeService.ApplyTheme(settings.Theme);

        Tabs = new ObservableCollection<ChatTabViewModel>();
        Players = new ObservableCollection<string> { AllPlayersValue };
        Channels = new ObservableCollection<string> { AllChannelsValue };
        AvailableLogDates = new ObservableCollection<DateTime>();
        NotificationChannels = new ObservableCollection<ChatFilterOption>();
        ResetNotificationChannels();
        NotificationTriggerModes = new ObservableCollection<string>
        {
            NotificationTriggerKinds.TextContains,
            NotificationTriggerKinds.PrivateMessage,
            NotificationTriggerKinds.PlayerDescriptor
        };
        NotificationRules = new ObservableCollection<NotificationRule>(settings.NotificationRules ?? new List<NotificationRule>());
        NormalizeNotificationRuleChannels();
        AvailableFontFamilies = new ObservableCollection<string>(
            Media.Fonts.SystemFontFamilies
                .Select(x => x.Source)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
        AvailableFontSizes = new ObservableCollection<double> { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24 };
        AvailableFontWeights = new ObservableCollection<string> { "Light", "Normal", "SemiBold", "Bold" };
        AvailableFontStyles = new ObservableCollection<string> { "Normal", "Italic" };

        OpenFileCommand = new RelayCommand(OpenFileDialog);
        OpenTodayFileCommand = new RelayCommand(OpenTodayFile);
        BrowseLogsDirectoryCommand = new RelayCommand(BrowseLogsDirectory);
        BrowseNotificationSoundFileCommand = new RelayCommand(BrowseNotificationSoundFile);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ClearCommand = new RelayCommand(ClearMessages);
        OpenGameCommand = new RelayCommand(OpenGame);
        AddNotificationRuleCommand = new RelayCommand(AddNotificationRule);
        RemoveNotificationRuleCommand = new RelayCommand<NotificationRule>(RemoveNotificationRule);
        TestNotificationSoundCommand = new RelayCommand(TestNotificationSound);

        AddTab(AllTabName);
        _ = VisibleTabs;
        SelectedTab = Tabs.FirstOrDefault();

        timer = new System.Threading.Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);

        ApplyKeepAwakeState();

        _ = InitializeAsync();
    }

    public ObservableCollection<ChatTabViewModel> Tabs { get; }

    public ICollectionView VisibleTabs
    {
        get
        {
            if (visibleTabs is null)
            {
                visibleTabs = CollectionViewSource.GetDefaultView(Tabs);
                visibleTabs.Filter = item => item is ChatTabViewModel tab && ShouldShowTab(tab);
            }

            return visibleTabs;
        }
    }

    public ObservableCollection<string> Players { get; }

    public ObservableCollection<string> Channels { get; }

    public ObservableCollection<DateTime> AvailableLogDates { get; }

    public ObservableCollection<ChatFilterOption> NotificationChannels { get; }

    public ObservableCollection<string> NotificationTriggerModes { get; }

    public ObservableCollection<NotificationRule> NotificationRules { get; }

    public ObservableCollection<string> AvailableFontFamilies { get; }

    public ObservableCollection<double> AvailableFontSizes { get; }

    public ObservableCollection<string> AvailableFontWeights { get; }

    public ObservableCollection<string> AvailableFontStyles { get; }

    public RelayCommand OpenFileCommand { get; }

    public RelayCommand OpenTodayFileCommand { get; }

    public RelayCommand BrowseLogsDirectoryCommand { get; }

    public RelayCommand BrowseNotificationSoundFileCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    public RelayCommand ToggleThemeCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand OpenGameCommand { get; }

    public RelayCommand AddNotificationRuleCommand { get; }

    public RelayCommand<NotificationRule> RemoveNotificationRuleCommand { get; }

    public RelayCommand TestNotificationSoundCommand { get; }

    public ChatTabViewModel? SelectedTab
    {
        get => selectedTab;
        set
        {
            if (SetProperty(ref selectedTab, value))
            {
                OnPropertyChanged(nameof(PrivateColumnsVisibility));
            }
        }
    }

    public System.Windows.Visibility PrivateColumnsVisibility => string.Equals(
        selectedTab?.Name,
        "Private",
        StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public string? SelectedPlayer
    {
        get => selectedPlayer;
        set
        {
            if (suppressFilterChange)
            {
                return;
            }

            string? normalizedValue = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value;

            if (SetProperty(ref selectedPlayer, normalizedValue))
            {
                OnPropertyChanged(nameof(CanSearchWholeHistory));

                if (!CanSearchWholeHistory && SearchWholeHistory)
                {
                    SearchWholeHistory = false;
                    return;
                }

                RefreshFiltersOrReloadFromDatabase();
            }
        }
    }

    public bool FilterPlayerByDescriptor
    {
        get => filterPlayerByDescriptor;
        set
        {
            if (SetProperty(ref filterPlayerByDescriptor, value))
            {
                if (!SearchWholeHistory)
                {
                    RebuildPlayersFromMessages();
                }

                RefreshFiltersOrReloadFromDatabase();
            }
        }
    }

    public bool IsSettingsExpanded
    {
        get => isSettingsExpanded;
        set => SetProperty(ref isSettingsExpanded, value);
    }

    public bool IsNotificationsExpanded
    {
        get => isNotificationsExpanded;
        set => SetProperty(ref isNotificationsExpanded, value);
    }

    public string? SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (suppressFilterChange)
            {
                return;
            }

            if (SetProperty(ref selectedChannel, value))
            {
                RefreshFiltersOrReloadFromDatabase();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSearchWholeHistory));

                if (!CanSearchWholeHistory && SearchWholeHistory)
                {
                    SearchWholeHistory = false;
                    return;
                }

                RefreshFiltersOrReloadFromDatabase();
            }
        }
    }

    public DateTime? SelectedLogDate
    {
        get => selectedLogDate;
        set
        {
            DateTime? normalizedValue = value?.Date;
            if (SetProperty(ref selectedLogDate, normalizedValue)
                && !suppressLogDateReload
                && normalizedValue.HasValue)
            {
                _ = LoadMessagesFromDatabaseAsync("Загрузка сообщений");
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string CurrentFilePath
    {
        get => currentFilePath;
        private set => SetProperty(ref currentFilePath, value);
    }

    public bool IsLoading
    {
        get => isLoading;
        private set => SetProperty(ref isLoading, value);
    }

    public int LoadingProgress
    {
        get => loadingProgress;
        private set => SetProperty(ref loadingProgress, value);
    }

    public string LoadingTitle
    {
        get => loadingTitle;
        private set => SetProperty(ref loadingTitle, value);
    }

    public string LoadingProgressText
    {
        get => loadingProgressText;
        private set => SetProperty(ref loadingProgressText, value);
    }

    public string LoadingText
    {
        get => loadingText;
        private set => SetProperty(ref loadingText, value);
    }

    public string LogsDirectory
    {
        get => settings.LogsDirectory;
        set
        {
            if (settings.LogsDirectory != value)
            {
                settings.LogsDirectory = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public string FileNamePattern
    {
        get => settings.FileNamePattern;
        set
        {
            if (settings.FileNamePattern != value)
            {
                settings.FileNamePattern = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public int RefreshIntervalMs
    {
        get => settings.RefreshIntervalMs;
        set
        {
            int safeValue = Math.Max(500, value);
            if (settings.RefreshIntervalMs != safeValue)
            {
                settings.RefreshIntervalMs = safeValue;
                OnPropertyChanged();
                RestartTimer();
            }
        }
    }

    public bool OpenTodayFileOnStart
    {
        get => settings.OpenTodayFileOnStart;
        set
        {
            if (settings.OpenTodayFileOnStart != value)
            {
                settings.OpenTodayFileOnStart = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ReadExistingFileOnOpen
    {
        get => settings.ReadExistingFileOnOpen;
        set
        {
            if (settings.ReadExistingFileOnOpen != value)
            {
                settings.ReadExistingFileOnOpen = value;
                OnPropertyChanged();
            }
        }
    }


    public bool AutoSwitchToLatestTodayFile
    {
        get => settings.AutoSwitchToLatestTodayFile;
        set
        {
            if (settings.AutoSwitchToLatestTodayFile != value)
            {
                settings.AutoSwitchToLatestTodayFile = value;
                OnPropertyChanged();
            }
        }
    }

    public bool KeepMessagesWhenLogFileChanges
    {
        get => settings.KeepMessagesWhenLogFileChanges;
        set
        {
            if (settings.KeepMessagesWhenLogFileChanges != value)
            {
                settings.KeepMessagesWhenLogFileChanges = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ClearMessagesWhenDateChanges
    {
        get => settings.ClearMessagesWhenDateChanges;
        set
        {
            if (settings.ClearMessagesWhenDateChanges != value)
            {
                settings.ClearMessagesWhenDateChanges = value;
                OnPropertyChanged();
            }
        }
    }

    public bool DeleteImportedLogFiles
    {
        get => settings.DeleteImportedLogFiles;
        set
        {
            if (settings.DeleteImportedLogFiles != value)
            {
                settings.DeleteImportedLogFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SearchWholeHistory
    {
        get => searchWholeHistory;
        set
        {
            bool safeValue = value && CanSearchWholeHistory;
            if (SetProperty(ref searchWholeHistory, safeValue))
            {
                _ = LoadMessagesFromDatabaseAsync("Загрузка сообщений");
            }
        }
    }

    public bool CanSearchWholeHistory => !IsAllPlayersSelected() || HasSearchText();

    public bool KeepGameActive
    {
        get => settings.KeepGameActive;
        set
        {
            if (settings.KeepGameActive != value)
            {
                settings.KeepGameActive = value;
                OnPropertyChanged();
                ApplyKeepAwakeState();
            }
        }
    }

    public bool AntiAway
    {
        get => settings.AntiAway;
        set
        {
            if (settings.AntiAway != value)
            {
                settings.AntiAway = value;
                OnPropertyChanged();
            }
        }
    }

    public int KeepGameActiveIntervalMinutes
    {
        get => settings.KeepGameActiveIntervalMinutes;
        set
        {
            int safeValue = Math.Clamp(value, 1, 180);
            if (settings.KeepGameActiveIntervalMinutes != safeValue)
            {
                settings.KeepGameActiveIntervalMinutes = safeValue;
                OnPropertyChanged();
            }
        }
    }

    public bool NotificationsEnabled
    {
        get => settings.NotificationsEnabled;
        set
        {
            if (value && string.IsNullOrWhiteSpace(CurrentFilePath))
            {
                System.Windows.MessageBox.Show(
                    "Уведомления можно включить только после открытия файла лога.",
                    "NCLA Chat Viewer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                settings.NotificationsEnabled = false;
                OnPropertyChanged();
                return;
            }

            if (settings.NotificationsEnabled != value)
            {
                settings.NotificationsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseSystemNotificationSound
    {
        get => settings.UseSystemNotificationSound;
        set
        {
            if (settings.UseSystemNotificationSound != value)
            {
                settings.UseSystemNotificationSound = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomNotificationSoundEnabled));
            }
        }
    }

    public bool IsCustomNotificationSoundEnabled => !settings.UseSystemNotificationSound;

    public string NotificationSoundFilePath
    {
        get => settings.NotificationSoundFilePath;
        set
        {
            if (settings.NotificationSoundFilePath != value)
            {
                settings.NotificationSoundFilePath = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public string TableFontFamily
    {
        get => settings.TableFontFamily;
        set
        {
            if (settings.TableFontFamily != value)
            {
                settings.TableFontFamily = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;
                OnPropertyChanged();
            }
        }
    }

    public double TableFontSize
    {
        get => settings.TableFontSize;
        set
        {
            double safeValue = Math.Clamp(value, 8, 36);
            if (Math.Abs(settings.TableFontSize - safeValue) > 0.01)
            {
                settings.TableFontSize = safeValue;
                OnPropertyChanged();
            }
        }
    }

    public string TableFontWeight
    {
        get => settings.TableFontWeight;
        set
        {
            if (settings.TableFontWeight != value)
            {
                settings.TableFontWeight = string.IsNullOrWhiteSpace(value) ? "Normal" : value;
                OnPropertyChanged();
            }
        }
    }

    public string TableFontStyle
    {
        get => settings.TableFontStyle;
        set
        {
            if (settings.TableFontStyle != value)
            {
                settings.TableFontStyle = string.IsNullOrWhiteSpace(value) ? "Normal" : value;
                OnPropertyChanged();
            }
        }
    }

    public string Theme
    {
        get => settings.Theme;
        set
        {
            if (settings.Theme != value)
            {
                settings.Theme = value;
                OnPropertyChanged();
                themeService.ApplyTheme(settings.Theme);
            }
        }
    }

    private async Task InitializeAsync()
    {
        await PromptForOldLogImportAsync();
        await RefreshAvailableLogDatesAsync();
        await SelectLogDateAsync(DateTime.Today, reload: true);

        if (settings.OpenTodayFileOnStart)
        {
            await OpenTodayFileAsync(reloadDate: false);
        }
        else
        {
            RestartTimer();
        }
    }

    private async Task PromptForOldLogImportAsync()
    {
        IReadOnlyList<string> filesToImport = await Task.Run(() => ChatFileResolver
            .GetLogFilePaths(settings.LogsDirectory, settings.FileNamePattern)
            .Where(chatDatabase.NeedsImport)
            .ToList());

        if (filesToImport.Count == 0)
        {
            return;
        }

        if (!ImportLogsDialogWindow.ConfirmImport(
                System.Windows.Application.Current.MainWindow,
                filesToImport.Count))
        {
            return;
        }

        try
        {
            await ImportLogFilesAsync(filesToImport, CancellationToken.None);
        }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusText = $"Ошибка импорта старых логов: {ex.Message}";

            ImportLogsDialogWindow.ShowNotice(
                System.Windows.Application.Current.MainWindow,
                "Импорт не завершен",
                "Импорт старых логов не был завершен. Файлы не удалялись, и их можно будет импортировать повторно при следующем запуске.");
        }
    }

    private async Task ImportLogFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        long[] fileWeights = await Task.Run(() => files
            .Select(path =>
            {
                if (!System.IO.File.Exists(path))
                {
                    return 1L;
                }

                long importedUntilByte = chatDatabase.GetImportedUntilByte(path);
                long remainingBytes = Math.Max(0, new FileInfo(path).Length - importedUntilByte);
                return Math.Max(1, remainingBytes);
            })
            .ToArray(), cancellationToken);

        long totalWeight = Math.Max(1, fileWeights.Sum());
        long completedWeight = 0;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsLoading = true;
            LoadingTitle = "Импорт старых логов";
            LoadingProgress = 0;
            LoadingProgressText = "0%";
            LoadingText = $"файл 0 из {files.Count}";
        }, Threading.DispatcherPriority.Background);

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = files[i];
            int currentFileNumber = i + 1;
            long fileWeight = fileWeights[i];
            long baseWeight = completedWeight;

            if (!System.IO.File.Exists(path))
            {
                completedWeight += fileWeight;
                continue;
            }

            var progress = new Progress<ChatLogReadProgress>(value =>
            {
                long currentFileWeight = value.TotalBytes > 0
                    ? Math.Min(fileWeight, Math.Max(0, value.ReadBytes))
                    : fileWeight;
                long importedWeight = Math.Min(totalWeight, baseWeight + currentFileWeight);
                int percent = (int)Math.Clamp(importedWeight * 100L / totalWeight, 0L, 100L);

                LoadingProgress = percent;
                LoadingProgressText = $"{percent}%";
                LoadingText = value.TotalLines > 0
                    ? $"файл {currentFileNumber} из {files.Count}: загружено {value.ParsedLines:N0} из {value.TotalLines:N0} строк"
                    : $"файл {currentFileNumber} из {files.Count}: загружено {value.ParsedLines:N0} строк";
            });

            await Task.Run(() =>
            {
                long startPosition = chatDatabase.GetImportedUntilByte(path);
                var reader = new ChatLogTailReader();
                reader.SetFile(path, startPosition);

                reader.ReadNewMessages(
                    ignoreCombatChats: true,
                    batch => chatDatabase.ImportMessages(path, batch),
                    progress,
                    cancellationToken);

                chatDatabase.UpdateImportedPosition(path, reader.LastPosition, markImportComplete: true);
            }, cancellationToken);

            completedWeight += fileWeight;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LoadingProgress = 100;
            LoadingProgressText = "100%";
            LoadingText = "импорт завершен";
            IsLoading = false;
        }, Threading.DispatcherPriority.Background);
    }

    private void OpenFileDialog()
    {
        var dialog = new Win32.OpenFileDialog
        {
            Filter = "Neverwinter chat log (*.log)|*.log|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _ = OpenFileAsync(dialog.FileName, clearCurrentMessages: true, autoFollowTodayFile: false);
        }
    }


    private void BrowseLogsDirectory()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выберите каталог с логами Neverwinter",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(LogsDirectory)
                ? LogsDirectory
                : ChatFileResolver.ResolveDirectory(LogsDirectory)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            LogsDirectory = dialog.SelectedPath;
            StatusText = $"Выбран каталог логов: {dialog.SelectedPath}";
        }
    }

    private void BrowseNotificationSoundFile()
    {
        var dialog = new Win32.OpenFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Выберите WAV-файл уведомления"
        };

        string currentPath = NotificationSoundFilePath;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            string? directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog() == true)
        {
            NotificationSoundFilePath = dialog.FileName;
            StatusText = $"Выбран звук уведомления: {dialog.FileName}";
        }
    }

    private void OpenTodayFile()
    {
        _ = OpenTodayFileAsync(reloadDate: true);
    }

    private async Task OpenTodayFileAsync(bool reloadDate)
    {
        followedLogDate = DateTime.Today;

        if (reloadDate)
        {
            await SelectLogDateAsync(followedLogDate, reload: true);
        }
        string? path = ChatFileResolver.GetFilePathForDate(settings.LogsDirectory, settings.FileNamePattern, followedLogDate);

        if (path is null)
        {
            autoFollowTodayFile = true;
            StatusText = $"Файл за текущую дату не найден. Каталог: {ChatFileResolver.ResolveDirectory(settings.LogsDirectory)}";
            RestartTimer();
            return;
        }

        await OpenFileAsync(path, clearCurrentMessages: false, autoFollowTodayFile: true, reloadDate: false);
    }

    private async Task OpenFileAsync(
        string path,
        bool clearCurrentMessages,
        bool autoFollowTodayFile,
        bool reloadDate = true)
    {
        if (!System.IO.File.Exists(path))
        {
            StatusText = $"Файл не найден: {path}";
            return;
        }

        this.autoFollowTodayFile = autoFollowTodayFile;

        if (autoFollowTodayFile)
        {
            followedLogDate = DateTime.Today;
        }
        else
        {
            DateTime? fileDate = ChatFileResolver.TryGetLogFileDate(path)?.Date;
            if (fileDate.HasValue)
            {
                followedLogDate = fileDate.Value;
            }
        }

        if (clearCurrentMessages && reloadDate)
        {
            await SelectLogDateAsync(followedLogDate, reload: true);
        }

        PrepareTailReaderForFile(path);
        CurrentFilePath = path;
        StatusText = $"Открыт файл: {path}";
        RestartTimer();
        await ReadNewMessagesAsync();
    }

    private async Task<bool> TrySwitchToLatestTodayFileIfNeededAsync()
    {
        if (!autoFollowTodayFile || !settings.AutoSwitchToLatestTodayFile)
        {
            return false;
        }

        DateTime today = DateTime.Today;
        bool dateChanged = followedLogDate.Date != today;

        if (dateChanged)
        {
            followedLogDate = today;

            if (settings.ClearMessagesWhenDateChanges)
            {
                await SelectLogDateAsync(today, reload: !SearchWholeHistory);
            }
        }

        string? latestPath = ChatFileResolver.GetFilePathForDate(settings.LogsDirectory, settings.FileNamePattern, today);
        if (latestPath is null)
        {
            return false;
        }

        if (string.Equals(latestPath, tailReader.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? previousPath = tailReader.FilePath;
        if (!string.IsNullOrWhiteSpace(previousPath) && System.IO.File.Exists(previousPath))
        {
            bool allowNotificationsForRemainder = settings.NotificationsEnabled
                && !string.IsNullOrWhiteSpace(CurrentFilePath);

            await ReadCurrentTailIntoDatabaseAsync(
                showProgress: false,
                allowNotifications: allowNotificationsForRemainder,
                CancellationToken.None);
        }

        bool clearCurrentMessages = !settings.KeepMessagesWhenLogFileChanges;
        if (clearCurrentMessages)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => ClearMessagesCore(resetFilters: false),
                Threading.DispatcherPriority.Background);
        }

        PrepareTailReaderForFile(latestPath);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentFilePath = latestPath;
            StatusText = $"Переключен актуальный файл: {latestPath}";
        }, Threading.DispatcherPriority.Background);

        TryDeleteImportedLogFile(previousPath);
        return true;
    }

    private void PrepareTailReaderForFile(string path)
    {
        long startPosition = chatDatabase.GetImportedUntilByte(path);

        chatDatabase.EnsureLogFile(path, startPosition);
        tailReader.SetFile(path, startPosition);
    }

    private void SaveSettings()
    {
        SaveSettingsCore();
        StatusText = "Настройки сохранены";
    }

    private void SaveSettingsCore()
    {
        settings.NotificationRules = NotificationRules.ToList();
        settingsService.Save(settings);
    }

    private void ToggleTheme()
    {
        Theme = string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";

        SaveSettings();
    }

    private async void OnTimerTick(object? state)
    {
        if (disposed)
        {
            return;
        }

        await ReadNewMessagesAsync();
    }

    private async Task ReadNewMessagesAsync()
    {
        if (!await readLock.WaitAsync(0))
        {
            return;
        }

        bool showProgress = false;

        try
        {
            bool switchedToAnotherFile = await TrySwitchToLatestTodayFileIfNeededAsync();
            showProgress = (settings.ReadExistingFileOnOpen && Tabs.FirstOrDefault(x => x.Name == AllTabName)?.TotalCount == 0)
                || switchedToAnotherFile;

            if (showProgress)
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingProgressText = "0%";
                LoadingTitle = "Чтение файла";
                LoadingText = "загружено 0 из 0 строк";
            }

            bool allowNotifications = settings.NotificationsEnabled
                && !showProgress
                && !string.IsNullOrWhiteSpace(CurrentFilePath);

            int addedCount = await ReadCurrentTailIntoDatabaseAsync(
                showProgress,
                allowNotifications,
                CancellationToken.None);

            if (addedCount > 0)
            {
                await RefreshAvailableLogDatesAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateStatusText(), Threading.DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusText = $"Ошибка чтения файла: {ex.Message}");
        }
        finally
        {
            if (showProgress)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LoadingProgress = 100;
                    LoadingProgressText = "100%";
                    LoadingText = "загрузка завершена";
                    IsLoading = false;
                    UpdateStatusText();
                });
            }

            readLock.Release();
        }
    }

    private async Task<int> ReadCurrentTailIntoDatabaseAsync(
        bool showProgress,
        bool allowNotifications,
        CancellationToken cancellationToken)
    {
        string? path = tailReader.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return 0;
        }

        var progress = showProgress
            ? new Progress<ChatLogReadProgress>(value =>
            {
                LoadingProgress = value.Percent;
                LoadingProgressText = $"{value.Percent}%";
                LoadingText = value.TotalLines > 0
                    ? $"загружено {value.ParsedLines:N0} из {value.TotalLines:N0} строк, сообщений {value.AcceptedMessages:N0}"
                    : $"загружено {value.ParsedLines:N0} строк, сообщений {value.AcceptedMessages:N0}";
            })
            : null;

        int insertedCount = 0;

        await Task.Run(() =>
        {
            tailReader.ReadNewMessages(
                ignoreCombatChats: true,
                batch =>
                {
                    IReadOnlyList<ChatMessage> inserted = chatDatabase.ImportMessages(path, batch);
                    if (inserted.Count == 0)
                    {
                        return;
                    }

                    insertedCount += inserted.Count;

                    System.Windows.Application.Current.Dispatcher.Invoke(
                        () => AddImportedMessagesToCurrentView(
                            inserted,
                            allowNotifications,
                            handleAntiAway: !showProgress),
                        Threading.DispatcherPriority.Background);
                },
                progress,
                cancellationToken);

            chatDatabase.UpdateImportedPosition(path, tailReader.LastPosition, markImportComplete: true);
        }, cancellationToken);

        return insertedCount;
    }

    private void AddImportedMessagesToCurrentView(
        IReadOnlyList<ChatMessage> messages,
        bool notify,
        bool handleAntiAway)
    {
        List<ChatMessage> visibleMessages = messages
            .Where(IsMessageInCurrentScope)
            .ToList();

        if (visibleMessages.Count == 0)
        {
            return;
        }

        AddMessages(
            visibleMessages,
            updateStatus: false,
            notify: notify,
            handleAntiAway: handleAntiAway);
    }

    private bool IsMessageInCurrentScope(ChatMessage message)
    {
        if (SearchWholeHistory)
        {
            return FilterMessage(message);
        }

        DateTime date = SelectedLogDate?.Date ?? DateTime.Today;
        return message.Time >= date && message.Time < date.AddDays(1);
    }

    private async Task SelectLogDateAsync(DateTime date, bool reload)
    {
        suppressLogDateReload = true;
        SelectedLogDate = date.Date;
        suppressLogDateReload = false;

        if (reload)
        {
            await LoadMessagesFromDatabaseAsync("Загрузка сообщений");
        }
    }

    private async Task RefreshAvailableLogDatesAsync()
    {
        IReadOnlyList<DateTime> dates = await Task.Run(chatDatabase.GetAvailableLogDates);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            AvailableLogDates.Clear();

            foreach (DateTime date in dates)
            {
                AvailableLogDates.Add(date.Date);
            }

            OnPropertyChanged(nameof(AvailableLogDates));
        }, Threading.DispatcherPriority.Background);
    }

    private async Task LoadMessagesFromDatabaseAsync(string loadingTitle)
    {
        databaseLoadCancellation?.Cancel();
        databaseLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = databaseLoadCancellation.Token;

        try
        {
            await loadLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            ChatMessageQuery query = BuildCurrentQuery();
            ChatMessageQuery playerOptionsQuery = BuildPlayerOptionsQuery(query);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsLoading = true;
                LoadingTitle = loadingTitle;
                LoadingProgress = 0;
                LoadingProgressText = "0%";
                LoadingText = "загружено 0 из 0 сообщений";
                ClearMessagesCore(resetFilters: false);
            }, Threading.DispatcherPriority.Background);

            IReadOnlyList<string> playerOptions = await Task.Run(
                () => chatDatabase.GetPlayers(playerOptionsQuery, FilterPlayerByDescriptor),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => ReplacePlayers(playerOptions),
                Threading.DispatcherPriority.Background,
                cancellationToken);

            var progress = new Progress<ChatDatabaseLoadProgress>(value =>
            {
                LoadingProgress = value.Percent;
                LoadingProgressText = $"{value.Percent}%";
                LoadingText = $"загружено {value.LoadedRows:N0} из {value.TotalRows:N0} сообщений";
            });

            await Task.Run(() => chatDatabase.LoadMessages(
                query,
                batch => System.Windows.Application.Current.Dispatcher.Invoke(
                    () => AddMessages(batch, updateStatus: false),
                    Threading.DispatcherPriority.Background),
                progress,
                cancellationToken), cancellationToken);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LoadingProgress = 100;
                LoadingProgressText = "100%";
                LoadingText = "загрузка завершена";
                IsLoading = false;
                UpdateStatusText();
            }, Threading.DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Новая загрузка уже стартовала, старый результат больше не нужен.
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsLoading = false;
                StatusText = $"Ошибка загрузки из базы: {ex.Message}";
            });
        }
        finally
        {
            loadLock.Release();
        }
    }

    private ChatMessageQuery BuildCurrentQuery()
    {
        DateTime? start = null;
        DateTime? end = null;

        if (!SearchWholeHistory)
        {
            start = (SelectedLogDate ?? DateTime.Today).Date;
            end = start.Value.AddDays(1);
        }

        if (!SearchWholeHistory)
        {
            return new ChatMessageQuery
            {
                StartInclusive = start,
                EndExclusive = end,
                FilterPlayerByDescriptor = FilterPlayerByDescriptor
            };
        }

        return new ChatMessageQuery
        {
            StartInclusive = start,
            EndExclusive = end,
            PlayerText = IsAllPlayersSelected() ? null : SelectedPlayer,
            FilterPlayerByDescriptor = FilterPlayerByDescriptor,
            ChannelName = IsAllChannelsSelected() ? null : SelectedChannel,
            MessageText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText
        };
    }

    private static ChatMessageQuery BuildPlayerOptionsQuery(ChatMessageQuery query)
    {
        return new ChatMessageQuery
        {
            StartInclusive = query.StartInclusive,
            EndExclusive = query.EndExclusive,
            ChannelName = query.ChannelName,
            MessageText = query.MessageText
        };
    }

    private void TryDeleteImportedLogFile(string? path)
    {
        if (!settings.DeleteImportedLogFiles
            || string.IsNullOrWhiteSpace(path)
            || string.Equals(path, CurrentFilePath, StringComparison.OrdinalIgnoreCase)
            || !System.IO.File.Exists(path)
            || !chatDatabase.IsLogFileSafelyImported(path))
        {
            return;
        }

        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось удалить импортированный лог: {ex.Message}";
        }
    }

    private void AddMessages(
        IReadOnlyList<ChatMessage> messages,
        bool updateStatus = true,
        bool notify = false,
        bool handleAntiAway = false)
    {
        bool shouldPlayNotification = false;
        var popupMessages = new List<(ChatMessage Message, bool DisappearingPopup)>();

        foreach (ChatMessage message in messages)
        {
            AddMessage(message);

            if (handleAntiAway)
            {
                _ = HandleAntiAwayIfNeeded(message);
            }

            if (!notify)
            {
                continue;
            }

            List<NotificationRule> matchedRules = GetMatchedNotificationRules(message);
            if (matchedRules.Count == 0)
            {
                continue;
            }

            if (matchedRules.Any(rule => rule.PlaySound))
            {
                shouldPlayNotification = true;
            }

            var popupRules = matchedRules.Where(rule => rule.ShowPopup).ToList();
            if (popupRules.Count > 0)
            {
                popupMessages.Add((
                    message,
                    popupRules.Any(rule => rule.DisappearingPopup)));
            }
        }

        RefreshVisibleTabs();

        if (shouldPlayNotification)
        {
            PlayConfiguredNotificationSound();
        }

        foreach ((ChatMessage message, bool disappearingPopup) in popupMessages.Take(5))
        {
            ShowNotificationPopup(message, disappearingPopup);
        }

        if (updateStatus)
        {
            UpdateStatusText();
        }
    }

    private void UpdateStatusText()
    {
        StatusText = $"Сообщений: {Tabs.FirstOrDefault(x => x.Name == AllTabName)?.TotalCount ?? 0}. Последнее чтение: {DateTime.Now:T}";
    }

    private async Task HandleAntiAwayIfNeeded(ChatMessage message)
    {
        if (!settings.AntiAway || !antiAwayService.IsAwayKickWarning(message))
        {
            return;
        }

        try
        {
            var errorMessage = await antiAwayService.HandleAwayKickWarningAsync(message);

            StatusText = string.IsNullOrWhiteSpace(errorMessage)
                ? $"AntiAway: обработано предупреждение о бездействии ({message.Time:HH:mm:ss})"
                : errorMessage;
        }
        catch (Exception ex)
        {
            StatusText = $"AntiAway: ошибка обработки предупреждения: {ex.Message}";
        }
    }

    private void AddMessage(ChatMessage message)
    {
        ChatTabViewModel allTab = GetOrCreateTab(AllTabName);
        allTab.Add(message);

        string tabGroupName = ChatTypeDisplayService.GetTabGroupName(message.ChatType);

        if (!string.IsNullOrWhiteSpace(tabGroupName) && !string.Equals(tabGroupName, AllTabName, StringComparison.OrdinalIgnoreCase))
        {
            GetOrCreateTab(tabGroupName).Add(message);
            InsertSortedNotificationChannel(tabGroupName);
        }

        if (!string.IsNullOrWhiteSpace(message.Player) && message.Player != "@")
        {
            InsertSortedPlayer(message.Player);
        }

        if (!string.IsNullOrWhiteSpace(message.DisplayTarget) && message.DisplayTarget != "@")
        {
            InsertSortedPlayer(message.DisplayTarget);
        }

        string chatName = message.ChatName;
        if (!string.IsNullOrWhiteSpace(chatName) && chatName != "@")
        {
            InsertSorted(Channels, chatName, AllChannelsValue);
            InsertSortedNotificationChannel(chatName);
        }
    }

    private static void InsertSorted(ObservableCollection<string> collection, string value, string pinnedFirstValue)
    {
        if (collection.Any(x => string.Equals(x, value, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        int startIndex = collection.Count > 0 && string.Equals(collection[0], pinnedFirstValue, StringComparison.CurrentCultureIgnoreCase)
            ? 1
            : 0;

        for (int i = startIndex; i < collection.Count; i++)
        {
            if (StringComparer.CurrentCultureIgnoreCase.Compare(value, collection[i]) < 0)
            {
                collection.Insert(i, value);
                return;
            }
        }

        collection.Add(value);
    }

    private void InsertSortedPlayer(string value)
    {
        if (Players.Any(x => string.Equals(x, value, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        int startIndex = Players.Count > 0 && string.Equals(Players[0], AllPlayersValue, StringComparison.CurrentCultureIgnoreCase)
            ? 1
            : 0;

        for (int i = startIndex; i < Players.Count; i++)
        {
            if (ComparePlayers(value, Players[i]) < 0)
            {
                Players.Insert(i, value);
                return;
            }
        }

        Players.Add(value);
    }

    private int ComparePlayers(string left, string right)
    {
        if (FilterPlayerByDescriptor)
        {
            int descriptorCompare = StringComparer.CurrentCultureIgnoreCase.Compare(
                PlayerIdentityService.ExtractDescriptor(left),
                PlayerIdentityService.ExtractDescriptor(right));

            if (descriptorCompare != 0)
            {
                return descriptorCompare;
            }
        }

        return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
    }


    private void ResetNotificationChannels()
    {
        NotificationChannels.Clear();
        NotificationChannels.Add(new ChatFilterOption(AllChannelsValue, AllChannelsValue));

        // Логические типы добавляем сразу, потому что у некоторых строк лога поле ChatName пустое.
        // Например, Friend отображается отдельной вкладкой «Друзья», но не попадал в список уведомлений,
        // если строить список только по имени чата/канала.
        foreach (string chatType in new[]
        {
            "Private",
            "Channel",
            "Friend",
            "LookingForGroup",
            "Trade",
            "Zone",
            "Alliance",
            "Local",
            "Emote",
            "NPC",
            "System",
            "Inventory",
            "Reward",
            "LootRolls",
            "Admin",
            "Error"
        })
        {
            InsertSortedNotificationChannel(chatType);
        }
    }


    private void NormalizeNotificationRuleChannels()
    {
        foreach (NotificationRule rule in NotificationRules)
        {
            if (string.IsNullOrWhiteSpace(rule.ChatFilter)
                || string.Equals(rule.ChatFilter, AllChannelsValue, StringComparison.CurrentCultureIgnoreCase))
            {
                rule.ChatFilter = AllChannelsValue;
                continue;
            }

            string displayName = ChatTypeDisplayService.GetChannelDisplayName(rule.ChatFilter);
            ChatFilterOption? option = NotificationChannels.FirstOrDefault(x =>
                string.Equals(x.DisplayName, displayName, StringComparison.CurrentCultureIgnoreCase));

            if (option is not null)
            {
                rule.ChatFilter = option.Value;
            }
        }
    }

    private void InsertSortedNotificationChannel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string displayName = ChatTypeDisplayService.GetChannelDisplayName(value);

        // В списке уведомлений пользователю важна логическая группа чата, а не внутреннее имя канала из лога.
        // Например, Alliance и ALLIANCEID_DRIDER... оба отображаются как «Альянс».
        // Если оставлять оба значения, выпадающий список начинает дублировать «Альянс», «Зона», «Поиск группы», «Торговля».
        // Сопоставление при срабатывании уведомления выполняется не только по сырому значению,
        // но и по отображаемому имени, поэтому одного пункта на логическую группу достаточно.
        if (NotificationChannels.Any(x =>
                string.Equals(x.Value, value, StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(x.DisplayName, displayName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        var option = new ChatFilterOption(value, displayName);

        int startIndex = NotificationChannels.Count > 0
            && string.Equals(NotificationChannels[0].Value, AllChannelsValue, StringComparison.CurrentCultureIgnoreCase)
                ? 1
                : 0;

        for (int i = startIndex; i < NotificationChannels.Count; i++)
        {
            int compareResult = StringComparer.CurrentCultureIgnoreCase.Compare(displayName, NotificationChannels[i].DisplayName);
            if (compareResult < 0
                || (compareResult == 0 && StringComparer.CurrentCultureIgnoreCase.Compare(value, NotificationChannels[i].Value) < 0))
            {
                NotificationChannels.Insert(i, option);
                return;
            }
        }

        NotificationChannels.Add(option);
    }

    private bool ShouldShowTab(ChatTabViewModel tab)
    {
        if (string.Equals(tab.Name, AllTabName, StringComparison.OrdinalIgnoreCase))
        {
            return Tabs.Count == 1 || tab.FilteredCount > 0;
        }

        return tab.FilteredCount > 0;
    }

    private void RefreshVisibleTabs()
    {
        if (visibleTabs is null)
        {
            return;
        }

        visibleTabs.Refresh();

        if (SelectedTab is null || !ShouldShowTab(SelectedTab))
        {
            SelectedTab = visibleTabs.Cast<ChatTabViewModel>().FirstOrDefault();
        }
    }

    private ChatTabViewModel GetOrCreateTab(string name)
    {
        ChatTabViewModel? existingTab = Tabs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return existingTab ?? AddTab(name);
    }

    private ChatTabViewModel AddTab(string name)
    {
        var tab = new ChatTabViewModel(name, FilterMessage);
        Tabs.Add(tab);
        return tab;
    }

    private bool FilterMessage(ChatMessage message)
    {
        if (!IsPlayerMatched(message))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedChannel)
            && SelectedChannel != AllChannelsValue
            && !string.Equals(message.ChatName, SelectedChannel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText)
            && (message.Message?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
        {
            return false;
        }

        return true;
    }

    private bool IsPlayerMatched(ChatMessage message)
    {
        if (IsAllPlayersSelected())
        {
            return true;
        }

        string filterText = SelectedPlayer?.Trim() ?? string.Empty;

        if (!FilterPlayerByDescriptor)
        {
            return ContainsText(message.Player, filterText)
                || ContainsText(message.DisplayTarget, filterText);
        }

        string selectedDescriptor = PlayerIdentityService.ExtractDescriptor(filterText);
        if (string.IsNullOrWhiteSpace(selectedDescriptor))
        {
            selectedDescriptor = filterText;
        }

        return ContainsText(PlayerIdentityService.ExtractDescriptor(message.Player), selectedDescriptor)
            || ContainsText(PlayerIdentityService.ExtractDescriptor(message.DisplayTarget), selectedDescriptor);
    }

    private static bool ContainsText(string? value, string filterText)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsAllPlayersSelected()
    {
        return IsAllPlayersValue(SelectedPlayer);
    }

    private bool IsAllChannelsSelected()
    {
        return IsAllChannelsValue(SelectedChannel);
    }

    private bool HasSearchText()
    {
        return !string.IsNullOrWhiteSpace(SearchText);
    }

    private static bool IsAllPlayersValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, AllPlayersValue, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool IsAllChannelsValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, AllChannelsValue, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RefreshFiltersOrReloadFromDatabase()
    {
        if (SearchWholeHistory)
        {
            _ = LoadMessagesFromDatabaseAsync("Загрузка сообщений");
            return;
        }

        RefreshFilters();
    }

    private void ClearMessages()
    {
        ClearMessagesCore(resetFilters: true);
    }

    private void ClearMessagesCore(bool resetFilters)
    {
        foreach (ChatTabViewModel tab in Tabs)
        {
            tab.Clear();
        }

        for (int i = Tabs.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(Tabs[i].Name, AllTabName, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.RemoveAt(i);
            }
        }

        ResetFilterOptions(resetFilters);

        if (resetFilters)
        {
            selectedPlayer = AllPlayersValue;
            selectedChannel = AllChannelsValue;
            searchText = string.Empty;
            searchWholeHistory = false;
            OnPropertyChanged(nameof(SelectedPlayer));
            OnPropertyChanged(nameof(CanSearchWholeHistory));
            OnPropertyChanged(nameof(SearchWholeHistory));
            OnPropertyChanged(nameof(SelectedChannel));
            OnPropertyChanged(nameof(SearchText));
        }

        RefreshFilters();
        SelectedTab = Tabs.FirstOrDefault();
    }

    private void ResetFilterOptions(bool resetFilters)
    {
        string? preservedPlayer = selectedPlayer;
        string? preservedChannel = selectedChannel;
        bool previousSuppressFilterChange = suppressFilterChange;

        suppressFilterChange = true;

        try
        {
            Players.Clear();
            Players.Add(AllPlayersValue);

            if (!resetFilters && !IsAllPlayersValue(preservedPlayer))
            {
                InsertSortedPlayer(preservedPlayer!);
            }

            Channels.Clear();
            Channels.Add(AllChannelsValue);
            ResetNotificationChannels();

            if (!resetFilters && !IsAllChannelsValue(preservedChannel))
            {
                InsertSorted(Channels, preservedChannel!, AllChannelsValue);
                InsertSortedNotificationChannel(preservedChannel!);
            }
        }
        finally
        {
            suppressFilterChange = previousSuppressFilterChange;
        }

        if (resetFilters)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedPlayer));
        OnPropertyChanged(nameof(CanSearchWholeHistory));
        OnPropertyChanged(nameof(SelectedChannel));
    }

    private void ReplacePlayers(IReadOnlyList<string> players)
    {
        string? preservedPlayer = selectedPlayer;
        bool previousSuppressFilterChange = suppressFilterChange;

        suppressFilterChange = true;

        try
        {
            Players.Clear();
            Players.Add(AllPlayersValue);

            if (!IsAllPlayersValue(preservedPlayer)
                && !players.Any(player => string.Equals(player, preservedPlayer, StringComparison.CurrentCultureIgnoreCase)))
            {
                InsertSortedPlayer(preservedPlayer!);
            }

            foreach (string player in players)
            {
                InsertSortedPlayer(player);
            }
        }
        finally
        {
            suppressFilterChange = previousSuppressFilterChange;
        }

        OnPropertyChanged(nameof(SelectedPlayer));
        OnPropertyChanged(nameof(CanSearchWholeHistory));
    }

    private void RefreshFilters()
    {
        foreach (ChatTabViewModel tab in Tabs)
        {
            tab.Refresh();
        }

        RefreshVisibleTabs();
    }

    private void RebuildPlayersFromMessages()
    {
        var players = Tabs
            .FirstOrDefault(x => x.Name == AllTabName)?
            .Messages
            .SelectMany(message => new[] { message.Player, message.DisplayTarget })
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "@")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, Comparer<string>.Create(ComparePlayers))
            .ToList() ?? new List<string>();

        ReplacePlayers(players);
    }

    private void RestartTimer()
    {
        if (disposed)
        {
            return;
        }

        int interval = Math.Max(500, settings.RefreshIntervalMs);
        timer.Change(interval, interval);
    }

    private List<NotificationRule> GetMatchedNotificationRules(ChatMessage message)
    {
        if (!settings.NotificationsEnabled || NotificationRules.Count == 0)
        {
            return new List<NotificationRule>();
        }

        return NotificationRules
            .Where(rule => notificationService.ShouldNotify(message, rule))
            .ToList();
    }

    private void ShowNotificationPopup(ChatMessage message, bool disappearingPopup)
    {
        try
        {
            var popup = new NotificationPopupWindow(
                NotificationPopupData.FromMessage(message),
                OpenGame,
                disappearingPopup)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            popup.Show();
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось показать уведомление: {ex.Message}";
        }
    }

    private void AddNotificationRule()
    {
        NotificationRules.Add(new NotificationRule
        {
            IsEnabled = true,
            TriggerKind = NotificationTriggerKinds.TextContains,
            ChatFilter = AllChannelsValue,
            Phrase = string.Empty,
            PlaySound = true,
            ShowPopup = true,
            DisappearingPopup = false
        });
    }

    private void RemoveNotificationRule(NotificationRule? rule)
    {
        if (rule is not null)
        {
            NotificationRules.Remove(rule);
        }
    }

    private void TestNotificationSound()
    {
        PlayConfiguredNotificationSound();
    }

    private void PlayConfiguredNotificationSound()
    {
        if (notificationService.PlayNotificationSound(
                settings.UseSystemNotificationSound,
                settings.NotificationSoundFilePath,
                out string? errorMessage))
        {
            StatusText = settings.UseSystemNotificationSound
                ? "Системный звук уведомления отправлен"
                : "Пользовательский звук уведомления отправлен";
            return;
        }

        StatusText = errorMessage ?? "Не удалось проиграть звук уведомления";
    }

    private void OpenGame()
    {
        if (!IsAdministrator())
        {
            System.Windows.MessageBox.Show(
                "Чтобы приложение могло развернуть игру, необходимо запустить его с правами администратора.",
                "NCLA Chat Viewer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (gameWindowService.TryActivateGameWindow(out string status))
        {
            StatusText = status;
            return;
        }

        System.Windows.MessageBox.Show(
            status,
            "NCLA Chat Viewer",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private void ApplyKeepAwakeState()
    {
        try
        {
            if (settings.KeepGameActive)
            {
                activityKeepAliveService.KeepSystemAwakeOnce();
            }
            else
            {
                activityKeepAliveService.Reset();
            }
        }
        catch
        {
            // Управление энергосбережением не должно ломать чтение логов и интерфейс.
        }
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            SaveSettingsCore();
        }
        catch
        {
            // Ошибка сохранения настроек при закрытии не должна мешать закрытию приложения.
        }

        timer.Dispose();
        databaseLoadCancellation?.Cancel();
        databaseLoadCancellation?.Dispose();
        activityKeepAliveService.Reset();
        readLock.Dispose();
        loadLock.Dispose();
    }
}
