using System.IO;
using System.Buffers;
using System.Text;
using NclaChatViewer.Models;

namespace NclaChatViewer.Services;

public sealed class ChatLogTailReader
{
    private const int ProgressReportLineInterval = 1000;
    private const int BatchSize = 1000;

    private string? filePath;
    private long lastPosition;
    private string tailRemainder = string.Empty;

    public string? FilePath => filePath;

    public long LastPosition => lastPosition;

    public void SetFile(string path, bool readExistingContent)
    {
        long startPosition = !readExistingContent && System.IO.File.Exists(path)
            ? new FileInfo(path).Length
            : 0;

        SetFile(path, startPosition);
    }

    public void SetFile(string path, long startPosition)
    {
        if (string.Equals(filePath, path, StringComparison.OrdinalIgnoreCase)
            && lastPosition == startPosition)
        {
            return;
        }

        filePath = path;
        tailRemainder = string.Empty;
        lastPosition = Math.Max(0, startPosition);
    }

    public IReadOnlyList<ChatMessage> ReadNewMessages(bool ignoreCombatChats)
    {
        var result = new List<ChatMessage>();
        _ = ReadNewMessages(ignoreCombatChats, batch => result.AddRange(batch), progress: null, cancellationToken: default);
        return result;
    }

    public int ReadNewMessages(
        bool ignoreCombatChats,
        Action<IReadOnlyList<ChatMessage>> onBatch,
        IProgress<ChatLogReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return 0;
        }

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (lastPosition > stream.Length)
        {
            lastPosition = 0;
            tailRemainder = string.Empty;
        }

        long startPosition = lastPosition;
        long totalBytesToRead = Math.Max(0, stream.Length - startPosition);
        long totalLinesToRead = progress is null ? 0 : CountLines(filePath, startPosition);

        if (totalBytesToRead == 0)
        {
            progress?.Report(new ChatLogReadProgress
            {
                TotalBytes = 1,
                ReadBytes = 1,
                TotalLines = 0,
                ParsedLines = 0
            });
            return 0;
        }

        stream.Seek(startPosition, SeekOrigin.Begin);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: true);

        var batch = new List<ChatMessage>(BatchSize);
        long parsedLines = 0;
        long acceptedMessages = 0;
        int resultCount = 0;

        if (!string.IsNullOrEmpty(tailRemainder))
        {
            string? firstLine = reader.ReadLine();
            string mergedLine = tailRemainder + (firstLine ?? string.Empty);
            tailRemainder = string.Empty;
            TryAddMessage(mergedLine, ignoreCombatChats, batch, ref acceptedMessages, ref resultCount);
            parsedLines++;
        }

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            parsedLines++;
            TryAddMessage(line, ignoreCombatChats, batch, ref acceptedMessages, ref resultCount);

            if (batch.Count >= BatchSize)
            {
                onBatch(batch.ToArray());
                batch.Clear();
            }

            if (parsedLines % ProgressReportLineInterval == 0)
            {
                progress?.Report(new ChatLogReadProgress
                {
                    TotalBytes = totalBytesToRead,
                    ReadBytes = Math.Clamp(stream.Position - startPosition, 0, totalBytesToRead),
                    TotalLines = totalLinesToRead,
                    ParsedLines = parsedLines,
                    AcceptedMessages = acceptedMessages
                });
            }
        }

        if (batch.Count > 0)
        {
            onBatch(batch.ToArray());
            batch.Clear();
        }

        lastPosition = stream.Position;

        progress?.Report(new ChatLogReadProgress
        {
            TotalBytes = totalBytesToRead,
            ReadBytes = totalBytesToRead,
            TotalLines = totalLinesToRead,
            ParsedLines = parsedLines,
            AcceptedMessages = acceptedMessages
        });

        return resultCount;
    }

    private static void TryAddMessage(
        string line,
        bool ignoreCombatChats,
        ICollection<ChatMessage> batch,
        ref long acceptedMessages,
        ref int resultCount)
    {
        if (!ChatLogParser.TryParseLine(line, out ChatMessage? message) || message is null)
        {
            return;
        }

        if (ignoreCombatChats && ChatTypeDisplayService.IsCombatChatType(message.ChatType))
        {
            return;
        }

        batch.Add(message);
        acceptedMessages++;
        resultCount++;
    }

    private static long CountLines(string path, long startPosition)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return 0;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (startPosition > stream.Length)
            {
                startPosition = 0;
            }

            stream.Seek(startPosition, SeekOrigin.Begin);

            long count = 0;
            bool sawAnyByte = false;
            byte lastByte = 0;
            int read;

            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sawAnyByte = true;

                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        count++;
                    }
                }

                lastByte = buffer[read - 1];
            }

            if (sawAnyByte && lastByte != (byte)'\n')
            {
                count++;
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
