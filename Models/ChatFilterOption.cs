namespace NclaChatViewer.Models;

public sealed class ChatFilterOption
{
    public ChatFilterOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }

    public string DisplayName { get; }

    public override string ToString()
    {
        return DisplayName;
    }
}
