using System.Data;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NclaChatViewer.Models;
using SQLitePCL;

namespace NclaChatViewer.Services;

public sealed class ChatDatabaseService
{
    private const int CurrentSchemaVersion = 3;
    private const int QueryBatchSize = 1000;

    private readonly string databasePath;

    static ChatDatabaseService()
    {
        Batteries_V2.Init();
    }

    public ChatDatabaseService(string? configuredDatabasePath, string settingsDirectory)
    {
        databasePath = ResolveDatabasePath(configuredDatabasePath, settingsDirectory);
        Initialize();
    }

    public string DatabasePath => databasePath;

    public long GetImportedUntilByte(string path)
    {
        string normalizedPath = NormalizePath(path);
        EnsureLogFile(path);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT imported_until_byte FROM log_files WHERE path = $path COLLATE NOCASE;";
        command.Parameters.AddWithValue("$path", normalizedPath);

        object? value = command.ExecuteScalar();
        long importedUntilByte = value is null || value == DBNull.Value
            ? 0
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);

        if (!File.Exists(path))
        {
            return 0;
        }

        long fileLength = new FileInfo(path).Length;
        return importedUntilByte > fileLength ? 0 : Math.Max(0, importedUntilByte);
    }

    public void EnsureLogFile(string path, long importedUntilByte = 0)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        EnsureLogFileCore(connection, transaction, path, importedUntilByte);
        transaction.Commit();
    }

    public bool NeedsImport(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return !IsLogFileSafelyImported(path);
    }

    public bool IsLogFileSafelyImported(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string normalizedPath = NormalizePath(path);
        FileInfo fileInfo = new(path);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT imported_until_byte,
                   length_bytes,
                   last_write_time_utc,
                   import_completed
              FROM log_files
             WHERE path = $path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$path", normalizedPath);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        long importedUntilByte = reader.GetInt64(0);
        long storedLengthBytes = reader.GetInt64(1);
        string storedLastWriteTimeUtc = reader.GetString(2);
        bool importCompleted = reader.GetInt64(3) != 0;
        string currentLastWriteTimeUtc = fileInfo.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);

        return importCompleted
            && importedUntilByte >= fileInfo.Length
            && storedLengthBytes == fileInfo.Length
            && string.Equals(storedLastWriteTimeUtc, currentLastWriteTimeUtc, StringComparison.Ordinal);
    }

    public void UpdateImportedPosition(string path, long importedUntilByte, bool markImportComplete = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string normalizedPath = NormalizePath(path);
        FileInfo? fileInfo = File.Exists(path) ? new FileInfo(path) : null;

        long safeImportedUntilByte = Math.Max(0, importedUntilByte);
        bool importCompleted = markImportComplete
            && fileInfo is not null
            && safeImportedUntilByte >= fileInfo.Length;

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE log_files
               SET imported_until_byte = $imported_until_byte,
                   length_bytes = $length_bytes,
                   last_write_time_utc = $last_write_time_utc,
                   import_completed = $import_completed,
                   import_completed_at_utc = $import_completed_at_utc,
                   updated_at_utc = $updated_at_utc
             WHERE path = $path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$imported_until_byte", safeImportedUntilByte);
        command.Parameters.AddWithValue("$length_bytes", fileInfo?.Length ?? 0);
        command.Parameters.AddWithValue("$last_write_time_utc", fileInfo?.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        command.Parameters.AddWithValue("$import_completed", importCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$import_completed_at_utc", importCompleted ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) : string.Empty);
        command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$path", normalizedPath);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ChatMessage> ImportMessages(string path, IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
        {
            return Array.Empty<ChatMessage>();
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        long fileId = EnsureLogFileCore(connection, transaction, path);
        var playerIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var channelIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var insertedMessages = new List<ChatMessage>(messages.Count);

        foreach (ChatMessage message in messages)
        {
            if (ChatTypeDisplayService.IsCombatChatType(message.ChatType))
            {
                continue;
            }

            long? playerId = GetOrCreatePlayerId(connection, transaction, playerIds, message.Player);
            long? targetPlayerId = GetOrCreatePlayerId(connection, transaction, playerIds, message.Target);
            long? channel1Id = GetOrCreateChannelId(connection, transaction, channelIds, message.ChannelName1);
            long? channel2Id = GetOrCreateChannelId(connection, transaction, channelIds, message.ChannelName2);
            long? chatNameId = GetOrCreateChannelId(connection, transaction, channelIds, message.ChatName);

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO chat_messages (
                    file_id,
                    source_index,
                    occurred_at_ticks,
                    occurred_date,
                    unknown_value,
                    player_id,
                    target_player_id,
                    channel1_id,
                    channel2_id,
                    chat_name_id,
                    chat_type,
                    tab_group,
                    message_text,
                    normalized_message_text,
                    raw_line,
                    raw_line_hash)
                VALUES (
                    $file_id,
                    $source_index,
                    $occurred_at_ticks,
                    $occurred_date,
                    $unknown_value,
                    $player_id,
                    $target_player_id,
                    $channel1_id,
                    $channel2_id,
                    $chat_name_id,
                    $chat_type,
                    $tab_group,
                    $message_text,
                    $normalized_message_text,
                    $raw_line,
                    $raw_line_hash);
                """;
            command.Parameters.AddWithValue("$file_id", fileId);
            command.Parameters.AddWithValue("$source_index", message.Index);
            command.Parameters.AddWithValue("$occurred_at_ticks", message.Time.Ticks);
            command.Parameters.AddWithValue("$occurred_date", message.Time.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$unknown_value", message.Unknown);
            AddNullableInt64(command, "$player_id", playerId);
            AddNullableInt64(command, "$target_player_id", targetPlayerId);
            AddNullableInt64(command, "$channel1_id", channel1Id);
            AddNullableInt64(command, "$channel2_id", channel2Id);
            AddNullableInt64(command, "$chat_name_id", chatNameId);
            command.Parameters.AddWithValue("$chat_type", message.ChatType);
            command.Parameters.AddWithValue("$tab_group", ChatTypeDisplayService.GetTabGroupName(message.ChatType));
            command.Parameters.AddWithValue("$message_text", message.Message);
            command.Parameters.AddWithValue("$normalized_message_text", PlayerIdentityService.NormalizeForSearch(message.Message));
            command.Parameters.AddWithValue("$raw_line", message.RawLine);
            command.Parameters.AddWithValue("$raw_line_hash", ComputeHash(message.RawLine));

            if (command.ExecuteNonQuery() > 0)
            {
                insertedMessages.Add(message);
            }
        }

        if (insertedMessages.Count > 0)
        {
            UpdateLogFileMessageBounds(connection, transaction, fileId, insertedMessages);
        }

        transaction.Commit();
        return insertedMessages;
    }

    public IReadOnlyList<DateTime> GetAvailableLogDates()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT occurred_date FROM chat_messages ORDER BY occurred_date DESC;";

        var dates = new List<DateTime>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (DateTime.TryParseExact(
                    reader.GetString(0),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime date))
            {
                dates.Add(date.Date);
            }
        }

        return dates;
    }

    public void LoadMessages(
        ChatMessageQuery query,
        Action<IReadOnlyList<ChatMessage>> onBatch,
        IProgress<ChatDatabaseLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using SqliteConnection connection = OpenConnection();

        QueryParts queryParts = BuildQueryParts(query);
        long totalRows = CountMessages(connection, queryParts);
        long loadedRows = 0;

        progress?.Report(new ChatDatabaseLoadProgress
        {
            TotalRows = totalRows,
            LoadedRows = 0
        });

        if (totalRows == 0)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.source_index,
                   m.occurred_at_ticks,
                   m.unknown_value,
                   COALESCE(p.name, '@') AS player_name,
                   COALESCE(tp.name, '@') AS target_name,
                   COALESCE(c1.name, '@') AS channel1_name,
                   COALESCE(c2.name, '@') AS channel2_name,
                   m.chat_type,
                   m.message_text,
                   m.raw_line
              FROM chat_messages m
              LEFT JOIN players p ON p.id = m.player_id
              LEFT JOIN players tp ON tp.id = m.target_player_id
              LEFT JOIN chat_channels c1 ON c1.id = m.channel1_id
              LEFT JOIN chat_channels c2 ON c2.id = m.channel2_id
             WHERE {queryParts.WhereClause}
             ORDER BY m.occurred_at_ticks, m.id;
            """;
        AddParameters(command, queryParts.Parameters);

        var batch = new List<ChatMessage>(QueryBatchSize);

        using SqliteDataReader reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            batch.Add(new ChatMessage
            {
                Index = reader.GetInt64(0),
                Time = new DateTime(reader.GetInt64(1)),
                Unknown = reader.GetInt32(2),
                Player = reader.GetString(3),
                Target = reader.GetString(4),
                ChannelName1 = reader.GetString(5),
                ChannelName2 = reader.GetString(6),
                ChatType = reader.GetString(7),
                Message = reader.GetString(8),
                RawLine = reader.GetString(9)
            });

            loadedRows++;

            if (batch.Count >= QueryBatchSize)
            {
                onBatch(batch.ToArray());
                batch.Clear();

                progress?.Report(new ChatDatabaseLoadProgress
                {
                    TotalRows = totalRows,
                    LoadedRows = loadedRows
                });
            }
        }

        if (batch.Count > 0)
        {
            onBatch(batch.ToArray());
        }

        progress?.Report(new ChatDatabaseLoadProgress
        {
            TotalRows = totalRows,
            LoadedRows = totalRows
        });
    }

    public IReadOnlyList<string> GetPlayers(ChatMessageQuery query, bool orderByDescriptor)
    {
        using SqliteConnection connection = OpenConnection();

        string orderBy = orderByDescriptor
            ? "normalized_descriptor, normalized_name, name"
            : "normalized_name, name";

        if (!query.StartInclusive.HasValue
            && !query.EndExclusive.HasValue
            && string.IsNullOrWhiteSpace(query.ChannelName)
            && string.IsNullOrWhiteSpace(query.MessageText))
        {
            using SqliteCommand allPlayersCommand = connection.CreateCommand();
            allPlayersCommand.CommandText = $"""
                SELECT name
                  FROM players
                 WHERE name <> ''
                 ORDER BY {orderBy};
                """;

            return ReadPlayerNames(allPlayersCommand);
        }

        QueryParts queryParts = BuildQueryParts(new ChatMessageQuery
        {
            StartInclusive = query.StartInclusive,
            EndExclusive = query.EndExclusive,
            ChannelName = query.ChannelName,
            MessageText = query.MessageText
        });

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT name
              FROM (
                    SELECT p.name AS name,
                           p.normalized_name AS normalized_name,
                           p.normalized_descriptor AS normalized_descriptor
                      FROM chat_messages m
                      JOIN players p ON p.id = m.player_id
                     WHERE {queryParts.WhereClause}
                       AND m.player_id IS NOT NULL

                    UNION

                    SELECT tp.name AS name,
                           tp.normalized_name AS normalized_name,
                           tp.normalized_descriptor AS normalized_descriptor
                      FROM chat_messages m
                      JOIN players tp ON tp.id = m.target_player_id
                     WHERE {queryParts.WhereClause}
                       AND m.target_player_id IS NOT NULL
                   )
             WHERE name <> ''
             ORDER BY {orderBy};
            """;
        AddParameters(command, queryParts.Parameters);

        return ReadPlayerNames(command);
    }

    private void Initialize()
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS log_files (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                file_name TEXT NOT NULL,
                log_date TEXT,
                length_bytes INTEGER NOT NULL DEFAULT 0,
                last_write_time_utc TEXT NOT NULL DEFAULT '',
                imported_until_byte INTEGER NOT NULL DEFAULT 0,
                import_completed INTEGER NOT NULL DEFAULT 0,
                import_completed_at_utc TEXT NOT NULL DEFAULT '',
                first_message_ticks INTEGER,
                last_message_ticks INTEGER,
                created_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                updated_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            );

            CREATE TABLE IF NOT EXISTS players (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                descriptor TEXT NOT NULL,
                normalized_name TEXT NOT NULL,
                normalized_descriptor TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS chat_channels (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                display_name TEXT NOT NULL,
                tab_group TEXT NOT NULL,
                normalized_name TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY,
                file_id INTEGER NOT NULL REFERENCES log_files(id) ON DELETE CASCADE,
                source_index INTEGER NOT NULL,
                occurred_at_ticks INTEGER NOT NULL,
                occurred_date TEXT NOT NULL,
                unknown_value INTEGER NOT NULL,
                player_id INTEGER REFERENCES players(id),
                target_player_id INTEGER REFERENCES players(id),
                channel1_id INTEGER REFERENCES chat_channels(id),
                channel2_id INTEGER REFERENCES chat_channels(id),
                chat_name_id INTEGER REFERENCES chat_channels(id),
                chat_type TEXT NOT NULL,
                tab_group TEXT NOT NULL,
                message_text TEXT NOT NULL,
                normalized_message_text TEXT NOT NULL,
                raw_line TEXT NOT NULL,
                raw_line_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                UNIQUE(file_id, raw_line_hash)
            );

            CREATE INDEX IF NOT EXISTS idx_log_files_log_date ON log_files(log_date);
            CREATE INDEX IF NOT EXISTS idx_players_normalized_name ON players(normalized_name);
            CREATE INDEX IF NOT EXISTS idx_players_normalized_descriptor ON players(normalized_descriptor);
            CREATE INDEX IF NOT EXISTS idx_channels_normalized_name ON chat_channels(normalized_name);
            CREATE INDEX IF NOT EXISTS idx_messages_date_time ON chat_messages(occurred_date, occurred_at_ticks, id);
            CREATE INDEX IF NOT EXISTS idx_messages_time ON chat_messages(occurred_at_ticks, id);
            CREATE INDEX IF NOT EXISTS idx_messages_player_time ON chat_messages(player_id, occurred_at_ticks, id);
            CREATE INDEX IF NOT EXISTS idx_messages_target_player_time ON chat_messages(target_player_id, occurred_at_ticks, id);
            CREATE INDEX IF NOT EXISTS idx_messages_date_player ON chat_messages(occurred_date, player_id);
            CREATE INDEX IF NOT EXISTS idx_messages_date_target_player ON chat_messages(occurred_date, target_player_id);
            CREATE INDEX IF NOT EXISTS idx_messages_channel_time ON chat_messages(chat_name_id, occurred_at_ticks, id);
            CREATE INDEX IF NOT EXISTS idx_messages_tab_time ON chat_messages(tab_group, occurred_at_ticks, id);
            CREATE INDEX IF NOT EXISTS idx_messages_chat_type ON chat_messages(chat_type);
            CREATE INDEX IF NOT EXISTS idx_messages_file ON chat_messages(file_id, id);

            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "log_files", "import_completed", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "log_files", "import_completed_at_utc", "TEXT NOT NULL DEFAULT ''");
        PurgeCombatMessages(connection);

        using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
        versionCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
        ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
        ExecutePragma(connection, "PRAGMA synchronous = NORMAL;");
        ExecutePragma(connection, "PRAGMA temp_store = MEMORY;");

        return connection;
    }

    private static void ExecutePragma(SqliteConnection connection, string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using (SqliteCommand checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = $"PRAGMA table_info({tableName});";
            using SqliteDataReader reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using SqliteCommand alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }

    private static void PurgeCombatMessages(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM chat_messages WHERE chat_type LIKE 'Combat%';";
        command.ExecuteNonQuery();
    }

    private static long EnsureLogFileCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string path,
        long importedUntilByte = 0)
    {
        string normalizedPath = NormalizePath(path);
        FileInfo? fileInfo = File.Exists(path) ? new FileInfo(path) : null;
        DateTime? logDate = ChatFileResolver.TryGetLogFileDate(path)?.Date;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO log_files (
                    path,
                    file_name,
                    log_date,
                    length_bytes,
                    last_write_time_utc,
                    imported_until_byte,
                    import_completed,
                    import_completed_at_utc,
                    updated_at_utc)
                VALUES (
                    $path,
                    $file_name,
                    $log_date,
                    $length_bytes,
                    $last_write_time_utc,
                    $imported_until_byte,
                    0,
                    '',
                    $updated_at_utc)
                ON CONFLICT(path) DO UPDATE SET
                    file_name = excluded.file_name,
                    log_date = excluded.log_date,
                    length_bytes = excluded.length_bytes,
                    last_write_time_utc = excluded.last_write_time_utc,
                    imported_until_byte = max(log_files.imported_until_byte, excluded.imported_until_byte),
                    import_completed = CASE
                        WHEN log_files.length_bytes <> excluded.length_bytes
                          OR log_files.last_write_time_utc <> excluded.last_write_time_utc
                        THEN 0
                        ELSE log_files.import_completed
                    END,
                    import_completed_at_utc = CASE
                        WHEN log_files.length_bytes <> excluded.length_bytes
                          OR log_files.last_write_time_utc <> excluded.last_write_time_utc
                        THEN ''
                        ELSE log_files.import_completed_at_utc
                    END,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$path", normalizedPath);
            command.Parameters.AddWithValue("$file_name", Path.GetFileName(path));
            command.Parameters.AddWithValue("$log_date", logDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
            command.Parameters.AddWithValue("$length_bytes", fileInfo?.Length ?? 0);
            command.Parameters.AddWithValue("$last_write_time_utc", fileInfo?.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            command.Parameters.AddWithValue("$imported_until_byte", Math.Max(0, importedUntilByte));
            command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        using SqliteCommand idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT id FROM log_files WHERE path = $path COLLATE NOCASE;";
        idCommand.Parameters.AddWithValue("$path", normalizedPath);
        return Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long? GetOrCreatePlayerId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, long> cache,
        string? playerName)
    {
        if (PlayerIdentityService.IsPlaceholder(playerName))
        {
            return null;
        }

        string name = playerName!.Trim();
        if (cache.TryGetValue(name, out long cachedId))
        {
            return cachedId;
        }

        string descriptor = PlayerIdentityService.ExtractDescriptor(name);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO players (
                    name,
                    descriptor,
                    normalized_name,
                    normalized_descriptor)
                VALUES (
                    $name,
                    $descriptor,
                    $normalized_name,
                    $normalized_descriptor);
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$descriptor", descriptor);
            command.Parameters.AddWithValue("$normalized_name", PlayerIdentityService.NormalizeForSearch(name));
            command.Parameters.AddWithValue("$normalized_descriptor", PlayerIdentityService.NormalizeForSearch(descriptor));
            command.ExecuteNonQuery();
        }

        using SqliteCommand idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT id FROM players WHERE name = $name COLLATE NOCASE;";
        idCommand.Parameters.AddWithValue("$name", name);
        long id = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        cache[name] = id;
        return id;
    }

    private static long? GetOrCreateChannelId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, long> cache,
        string? channelName)
    {
        if (PlayerIdentityService.IsPlaceholder(channelName))
        {
            return null;
        }

        string name = channelName!.Trim();
        if (cache.TryGetValue(name, out long cachedId))
        {
            return cachedId;
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO chat_channels (
                    name,
                    display_name,
                    tab_group,
                    normalized_name)
                VALUES (
                    $name,
                    $display_name,
                    $tab_group,
                    $normalized_name);
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$display_name", ChatTypeDisplayService.GetChannelDisplayName(name));
            command.Parameters.AddWithValue("$tab_group", ChatTypeDisplayService.GetTabGroupName(name));
            command.Parameters.AddWithValue("$normalized_name", PlayerIdentityService.NormalizeForSearch(name));
            command.ExecuteNonQuery();
        }

        using SqliteCommand idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT id FROM chat_channels WHERE name = $name COLLATE NOCASE;";
        idCommand.Parameters.AddWithValue("$name", name);
        long id = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        cache[name] = id;
        return id;
    }

    private static void UpdateLogFileMessageBounds(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        IReadOnlyList<ChatMessage> insertedMessages)
    {
        long minTicks = insertedMessages.Min(message => message.Time.Ticks);
        long maxTicks = insertedMessages.Max(message => message.Time.Ticks);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE log_files
               SET first_message_ticks = CASE
                       WHEN first_message_ticks IS NULL OR $min_ticks < first_message_ticks THEN $min_ticks
                       ELSE first_message_ticks
                   END,
                   last_message_ticks = CASE
                       WHEN last_message_ticks IS NULL OR $max_ticks > last_message_ticks THEN $max_ticks
                       ELSE last_message_ticks
                   END,
                   updated_at_utc = $updated_at_utc
             WHERE id = $file_id;
            """;
        command.Parameters.AddWithValue("$min_ticks", minTicks);
        command.Parameters.AddWithValue("$max_ticks", maxTicks);
        command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$file_id", fileId);
        command.ExecuteNonQuery();
    }

    private static long CountMessages(SqliteConnection connection, QueryParts queryParts)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM chat_messages m WHERE {queryParts.WhereClause};";
        AddParameters(command, queryParts.Parameters);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> ReadPlayerNames(SqliteCommand command)
    {
        var players = new List<string>();

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string player = reader.GetString(0);
            if (!PlayerIdentityService.IsPlaceholder(player))
            {
                players.Add(player);
            }
        }

        return players;
    }

    private static QueryParts BuildQueryParts(ChatMessageQuery query)
    {
        var conditions = new List<string>();
        var parameters = new List<QueryParameter>();

        if (query.StartInclusive.HasValue
            && query.EndExclusive.HasValue
            && query.EndExclusive.Value.Date == query.StartInclusive.Value.Date.AddDays(1)
            && query.StartInclusive.Value.TimeOfDay == TimeSpan.Zero
            && query.EndExclusive.Value.TimeOfDay == TimeSpan.Zero)
        {
            conditions.Add("m.occurred_date = $occurred_date");
            parameters.Add(new QueryParameter("$occurred_date", query.StartInclusive.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        else
        {
            if (query.StartInclusive.HasValue)
            {
                conditions.Add("m.occurred_at_ticks >= $start_ticks");
                parameters.Add(new QueryParameter("$start_ticks", query.StartInclusive.Value.Ticks));
            }

            if (query.EndExclusive.HasValue)
            {
                conditions.Add("m.occurred_at_ticks < $end_ticks");
                parameters.Add(new QueryParameter("$end_ticks", query.EndExclusive.Value.Ticks));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.PlayerText))
        {
            string playerText = query.FilterPlayerByDescriptor
                ? PlayerIdentityService.ExtractDescriptor(query.PlayerText)
                : query.PlayerText;

            if (string.IsNullOrWhiteSpace(playerText))
            {
                playerText = query.PlayerText;
            }

            string playerColumn = query.FilterPlayerByDescriptor
                ? "normalized_descriptor"
                : "normalized_name";
            string playerPattern = BuildLikePattern(PlayerIdentityService.NormalizeForSearch(playerText));

            conditions.Add($"""
                (
                    m.player_id IN (SELECT id FROM players WHERE {playerColumn} LIKE $player_pattern ESCAPE '\')
                    OR m.target_player_id IN (SELECT id FROM players WHERE {playerColumn} LIKE $player_pattern ESCAPE '\')
                )
                """);
            parameters.Add(new QueryParameter("$player_pattern", playerPattern));
        }

        if (!string.IsNullOrWhiteSpace(query.ChannelName))
        {
            conditions.Add("""
                m.chat_name_id IN (
                    SELECT id
                      FROM chat_channels
                     WHERE normalized_name = $channel_name
                )
                """);
            parameters.Add(new QueryParameter("$channel_name", PlayerIdentityService.NormalizeForSearch(query.ChannelName)));
        }

        if (!string.IsNullOrWhiteSpace(query.MessageText))
        {
            conditions.Add("m.normalized_message_text LIKE $message_pattern ESCAPE '\\'");
            parameters.Add(new QueryParameter("$message_pattern", BuildLikePattern(PlayerIdentityService.NormalizeForSearch(query.MessageText))));
        }

        return new QueryParts(
            conditions.Count == 0 ? "1 = 1" : string.Join(" AND ", conditions),
            parameters);
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<QueryParameter> parameters)
    {
        foreach (QueryParameter parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
    }

    private static void AddNullableInt64(SqliteCommand command, string name, long? value)
    {
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }

    private static string BuildLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('%');

        foreach (char ch in value)
        {
            if (ch is '%' or '_' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        builder.Append('%');
        return builder.ToString();
    }

    private static string ComputeHash(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ResolveDatabasePath(string? configuredDatabasePath, string settingsDirectory)
    {
        string path = string.IsNullOrWhiteSpace(configuredDatabasePath)
            ? Path.Combine(settingsDirectory, "chatlog.sqlite3")
            : configuredDatabasePath.Trim();

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(settingsDirectory, path));
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private readonly record struct QueryParameter(string Name, object? Value);

    private sealed record QueryParts(string WhereClause, IReadOnlyList<QueryParameter> Parameters);
}
