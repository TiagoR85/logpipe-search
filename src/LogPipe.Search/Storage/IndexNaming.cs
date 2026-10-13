using System.Globalization;

namespace LogPipe.Search.Storage;

/// <summary>Single source of truth for the daily-index naming scheme (ADR-0001).</summary>
public static class IndexNaming
{
    public const string Prefix = "logs-";
    public const string ReadPattern = "logs-*";
    public const string CurrentAlias = "logs-current";

    public static string IndexName(DateTimeOffset utcNow) =>
        Prefix + utcNow.UtcDateTime.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string indexName, out DateTime date)
    {
        date = default;
        if (!indexName.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        return DateTime.TryParseExact(indexName[Prefix.Length..], "yyyy.MM.dd",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }
}
