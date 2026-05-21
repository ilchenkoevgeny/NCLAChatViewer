using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Media = System.Windows.Media;
using NclaChatViewer.Models;
using NclaChatViewer.Services;

namespace NclaChatViewer.ViewModels;

public sealed class ChatTabViewModel : ViewModelBase
{
    private readonly Func<ChatMessage, bool> filter;

    public ChatTabViewModel(string name, Func<ChatMessage, bool> filter)
    {
        Name = name;
        DisplayName = ChatTypeDisplayService.GetDisplayName(name);
        AccentBrush = (Media.Brush)new Media.BrushConverter().ConvertFromString(ChatTypeDisplayService.GetAccentColor(name))!;
        this.filter = filter;
        Messages = new ObservableCollection<ChatMessage>();
        FilteredMessages = CollectionViewSource.GetDefaultView(Messages);
        FilteredMessages.Filter = item => item is ChatMessage message && this.filter(message);
    }

    public string Name { get; }

    public string DisplayName { get; }

    /// <summary>
    /// Признак вкладки личных сообщений.
    /// Используется интерфейсом, чтобы показывать колонки направления и адресата только там, где они имеют смысл.
    /// </summary>
    public bool IsPrivateTab => string.Equals(Name, "Private", StringComparison.OrdinalIgnoreCase);

    public Media.Brush AccentBrush { get; }

    public ObservableCollection<ChatMessage> Messages { get; }

    public ICollectionView FilteredMessages { get; }

    public int TotalCount => Messages.Count;

    private int filteredCount;

    public int FilteredCount
    {
        get => filteredCount;
        private set => SetProperty(ref filteredCount, value);
    }

    public void Add(ChatMessage message)
    {
        Messages.Add(message);

        if (filter(message))
        {
            FilteredCount++;
        }

        OnPropertyChanged(nameof(TotalCount));
    }

    public void Clear()
    {
        Messages.Clear();
        FilteredCount = 0;
        OnPropertyChanged(nameof(TotalCount));
    }

    public void Refresh()
    {
        FilteredMessages.Refresh();
        RecalculateFilteredCount();
    }

    public void RecalculateFilteredCount()
    {
        FilteredCount = Messages.Count(filter);
    }
}
