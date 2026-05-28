namespace NclaChatViewer.Services;

public sealed class ChatDatabaseLoadProgress
{
    public long TotalRows { get; set; }

    public long LoadedRows { get; set; }

    public int Percent
    {
        get
        {
            if (TotalRows <= 0)
            {
                return 100;
            }

            long value = LoadedRows * 100L / TotalRows;
            return (int)Math.Clamp(value, 0L, 100L);
        }
    }
}
