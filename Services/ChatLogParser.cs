using System.Globalization;
using NclaChatViewer.Models;

namespace NclaChatViewer.Services;

public static class ChatLogParser
{
    private const string DateFormat = "yyyyMMddHHmmss";

    public static bool TryParseLine(string line, out ChatMessage? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        int openIndex = line.IndexOf('[');
        int closeIndex = line.IndexOf(']');

        if (openIndex != 0 || closeIndex <= 0)
        {
            return false;
        }

        string header = line.Substring(1, closeIndex - 1);
        string text = closeIndex + 1 < line.Length
            ? line.Substring(closeIndex + 1)
            : string.Empty;

        string[] parts = header.Split(',');

        if (parts.Length < 8)
        {
            return false;
        }

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long index))
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                parts[1],
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime time))
        {
            return false;
        }

        _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int unknown);

        message = new ChatMessage
        {
            Index = index,
            Time = time,
            Unknown = unknown,
            Player = NormalizeValue(parts[3]),
            Target = NormalizeValue(parts[4]),
            ChannelName1 = NormalizeValue(parts[5]),
            ChannelName2 = NormalizeValue(parts[6]),
            ChatType = NormalizeValue(parts[7]),
            Message = text,
            RawLine = line
        };

        return true;
    }

    private static string NormalizeValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "@" : value.Trim();
    }
}
