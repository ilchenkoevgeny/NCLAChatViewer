using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace NclaChatViewer.Services;

public static class ChatFileResolver
{
    private static readonly Regex ChatLogDateRegex = new(
        @"chat_(?<date>\d{4}-\d{2}-\d{2})_(?<time>\d{2}-\d{2}-\d{2})\.log$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DateFormatPlaceholderRegex = new(
        @"\{0(?::[^}]*)?\}",
        RegexOptions.Compiled);

    public static string? GetTodayFilePath(string logsDirectory, string fileNamePattern)
    {
        return GetFilePathForDate(logsDirectory, fileNamePattern, DateTime.Today);
    }

    public static string? GetFilePathForDate(string logsDirectory, string fileNamePattern, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            return null;
        }

        string directory = ResolveDirectory(logsDirectory);

        if (!Directory.Exists(directory))
        {
            return null;
        }

        DateTime targetDate = date.Date;

        // Сначала пробуем строгий вариант по имени файла, например:
        // chat_2026-05-19_*.log
        string dateMask = BuildDateMask(fileNamePattern, targetDate);
        string? fileByNameDate = Directory
            .EnumerateFiles(directory, dateMask, SearchOption.TopDirectoryOnly)
            // Важно: актуальный файл определяем по времени изменения, а не по часу в имени.
            // Neverwinter может продолжать писать в файл с более ранним часом,
            // поэтому chat_..._00-00-00.log может быть актуальнее, чем chat_..._01-00-00.log.
            .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
            .ThenByDescending(GetLogFileDateTimeOrLastWriteTimeUtc)
            .FirstOrDefault();

        if (fileByNameDate is not null)
        {
            return fileByNameDate;
        }

        // Neverwinter может создавать файл с часом в UTC.
        // В этом случае после полуночи локального времени актуальный чат может называться
        // chat_2026-05-18_23-00-00.log, но изменён он уже 2026-05-19.
        // Поэтому fallback ищет самый свежий chat-лог, изменённый в целевую дату.
        string dateAgnosticMask = BuildDateAgnosticMask(fileNamePattern);

        return Directory
            .EnumerateFiles(directory, dateAgnosticMask, SearchOption.TopDirectoryOnly)
            .Where(path => System.IO.File.GetLastWriteTime(path).Date == targetDate)
            .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
            .ThenByDescending(GetLogFileDateTimeOrLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static IReadOnlyList<string> GetLogFilePaths(string logsDirectory, string fileNamePattern)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            return Array.Empty<string>();
        }

        string directory = ResolveDirectory(logsDirectory);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        string mask = BuildDateAgnosticMask(fileNamePattern);

        return Directory
            .EnumerateFiles(directory, mask, SearchOption.TopDirectoryOnly)
            .OrderBy(GetLogFileDateTimeOrLastWriteTimeUtc)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static DateTime? TryGetLogFileDate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        Match match = ChatLogDateRegex.Match(Path.GetFileName(path));
        if (!match.Success)
        {
            return null;
        }

        string value = $"{match.Groups["date"].Value}_{match.Groups["time"].Value}";
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd_HH-mm-ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime result)
            ? result
            : null;
    }

    public static string ResolveDirectory(string logsDirectory)
    {
        if (Path.IsPathRooted(logsDirectory))
        {
            return logsDirectory;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, logsDirectory));
    }

    private static string BuildDateMask(string fileNamePattern, DateTime date)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, fileNamePattern, date);
        }
        catch (FormatException)
        {
            return fileNamePattern;
        }
    }

    private static string BuildDateAgnosticMask(string fileNamePattern)
    {
        string mask = DateFormatPlaceholderRegex.Replace(fileNamePattern, "*");
        return string.IsNullOrWhiteSpace(mask) ? "chat_*.log" : mask;
    }

    private static DateTime GetLogFileDateTimeOrLastWriteTimeUtc(string path)
    {
        DateTime? logDateTime = TryGetLogFileDate(path);
        return logDateTime?.ToUniversalTime() ?? System.IO.File.GetLastWriteTimeUtc(path);
    }
}
