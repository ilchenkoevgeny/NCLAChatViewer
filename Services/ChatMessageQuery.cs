namespace NclaChatViewer.Services;

public sealed class ChatMessageQuery
{
    public DateTime? StartInclusive { get; init; }

    public DateTime? EndExclusive { get; init; }

    public string? PlayerText { get; init; }

    public bool FilterPlayerByDescriptor { get; init; }

    public string? ChannelName { get; init; }

    public string? MessageText { get; init; }
}
