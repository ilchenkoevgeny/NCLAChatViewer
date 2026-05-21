namespace NclaChatViewer.Services;

public static class ChatTypeDisplayService
{
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["All"] = "Все",
        ["System"] = "Система",
        ["CombatOther"] = "Бой — другое",
        ["CombatSelf"] = "Бой — свой",
        ["Inventory"] = "Инвентарь",
        ["LookingForGroup"] = "Поиск группы",
        ["NeighborhoodChange"] = "Смена зоны",
        ["NPC"] = "NPC",
        ["Alliance"] = "Альянс",
        ["Mission"] = "Задания",
        ["MRG"] = "Подбор группы",
        ["Friend"] = "Друзья",
        ["Trade"] = "Торговля",
        ["Reward"] = "Награды",
        ["Private"] = "Личные сообщения",
        ["Private_Sent"] = "Личные сообщения",
        ["Private_Received"] = "Личные сообщения",
        ["Local"] = "Локальный",
        ["LootRolls"] = "Розыгрыш добычи",
        ["Zone"] = "Зона",
        ["Emote"] = "Эмоции",
        ["Admin"] = "Админ",
        ["Error"] = "Ошибки",
        ["Channel"] = "Каналы"
    };

    private static readonly Dictionary<string, string> AccentColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["All"] = "#60A5FA",
        ["System"] = "#A78BFA",
        ["CombatOther"] = "#F97316",
        ["CombatSelf"] = "#EF4444",
        ["Inventory"] = "#22C55E",
        ["LookingForGroup"] = "#38BDF8",
        ["NeighborhoodChange"] = "#14B8A6",
        ["NPC"] = "#EAB308",
        ["Alliance"] = "#818CF8",
        ["Mission"] = "#F59E0B",
        ["MRG"] = "#06B6D4",
        ["Friend"] = "#84CC16",
        ["Trade"] = "#FACC15",
        ["Reward"] = "#F472B6",
        ["Private"] = "#C084FC",
        ["Private_Sent"] = "#C084FC",
        ["Private_Received"] = "#C084FC",
        ["Local"] = "#34D399",
        ["LootRolls"] = "#FB7185",
        ["Zone"] = "#7C8CFF",
        ["Emote"] = "#60A5FA",
        ["Admin"] = "#38BDF8",
        ["Error"] = "#DC2626",
        ["Channel"] = "#2DD4BF"
    };

    /// <summary>
    /// Возвращает логическую группу для вкладок.
    /// Например, Private и Private_Sent объединяются в одну вкладку, чтобы личную переписку можно было читать в хронологическом порядке.
    /// </summary>
    public static string GetTabGroupName(string? chatType)
    {
        if (string.IsNullOrWhiteSpace(chatType))
        {
            return "Unknown";
        }

        if (string.Equals(chatType, "Private", StringComparison.OrdinalIgnoreCase)
            || string.Equals(chatType, "Private_Sent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(chatType, "Private_Received", StringComparison.OrdinalIgnoreCase))
        {
            return "Private";
        }

        return chatType;
    }


    public static bool IsCombatChatType(string? chatType)
    {
        return !string.IsNullOrWhiteSpace(chatType)
            && chatType.StartsWith("Combat", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDisplayName(string? chatType)
    {
        if (string.IsNullOrWhiteSpace(chatType))
        {
            return "Без типа";
        }

        string groupName = GetTabGroupName(chatType);

        return DisplayNames.TryGetValue(groupName, out string? displayName)
            ? displayName
            : SplitPascalCase(groupName);
    }


    public static string GetChannelDisplayName(string? chatName)
    {
        if (string.IsNullOrWhiteSpace(chatName))
        {
            return "Все чаты";
        }

        string normalized = chatName.Trim();

        if (string.Equals(normalized, "Все чаты", StringComparison.OrdinalIgnoreCase))
        {
            return "Все чаты";
        }

        if (normalized.StartsWith("LookingForGroup", StringComparison.OrdinalIgnoreCase))
        {
            return "Поиск группы";
        }

        if (normalized.StartsWith("TRADE_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Trade", StringComparison.OrdinalIgnoreCase))
        {
            return "Торговля";
        }

        if (normalized.StartsWith("ZONE_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Zone", StringComparison.OrdinalIgnoreCase))
        {
            return "Зона";
        }

        if (normalized.StartsWith("ALLIANCEID_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Alliance", StringComparison.OrdinalIgnoreCase))
        {
            return "Альянс";
        }

        if (normalized.StartsWith("GUILDID_", StringComparison.OrdinalIgnoreCase))
        {
            return "Гильдия";
        }

        if (normalized.StartsWith("OFFICERID_", StringComparison.OrdinalIgnoreCase))
        {
            return "Офицеры гильдии";
        }

        if (normalized.StartsWith("MRG_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "MRG", StringComparison.OrdinalIgnoreCase))
        {
            return "Подбор группы";
        }

        if (normalized.StartsWith("Private", StringComparison.OrdinalIgnoreCase))
        {
            return "Личные сообщения";
        }

        return GetDisplayName(normalized) == SplitPascalCase(normalized)
            ? normalized
            : GetDisplayName(normalized);
    }

    public static string GetAccentColor(string? chatType)
    {
        if (string.IsNullOrWhiteSpace(chatType))
        {
            return "#60A5FA";
        }

        string groupName = GetTabGroupName(chatType);

        return AccentColors.TryGetValue(groupName, out string? color)
            ? color
            : "#60A5FA";
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var result = new List<char>(value.Length + 8);

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];

            if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[i - 1]))
            {
                result.Add(' ');
            }

            result.Add(current);
        }

        return new string(result.ToArray());
    }
}
