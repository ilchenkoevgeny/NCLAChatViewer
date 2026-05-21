namespace NclaChatViewer.Services;

public sealed class ChatLogReadProgress
{
    public long TotalBytes { get; set; }

    public long ReadBytes { get; set; }

    public long ParsedLines { get; set; }

    public long AcceptedMessages { get; set; }

    public int Percent
    {
        get
        {
            if (TotalBytes <= 0)
            {
                return 0;
            }

            long value = ReadBytes * 100L / TotalBytes;
            return (int)Math.Clamp(value, 0L, 100L);
        }
    }
}
