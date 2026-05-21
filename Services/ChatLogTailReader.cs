using System.IO;
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

    public void SetFile(string path, bool readExistingContent)
    {
        if (string.Equals(filePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        filePath = path;
        tailRemainder = string.Empty;

        if (!readExistingContent && System.IO.File.Exists(path))
        {
            lastPosition = new FileInfo(path).Length;
            return;
        }

        lastPosition = 0;
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

        if (totalBytesToRead == 0)
        {
            progress?.Report(new ChatLogReadProgress
            {
                TotalBytes = 1,
                ReadBytes = 1
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
}
