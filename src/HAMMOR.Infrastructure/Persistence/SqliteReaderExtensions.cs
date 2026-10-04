using Microsoft.Data.Sqlite;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>
/// Typed column readers. Timestamps round-trip as ISO-8601 round-trip ("O")
/// strings so ordering is lexicographic and timezone-safe.
/// </summary>
internal static class SqliteReaderExtensions
{
    internal const string TimestampFormat = "O";

    internal static string GetStringValue(this SqliteDataReader reader, string column) =>
        reader.GetString(reader.GetOrdinal(column));

    internal static string? GetNullableString(this SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    internal static int GetInt(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column));

    internal static bool GetBool(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column)) != 0;

    internal static long GetLong(this SqliteDataReader reader, string column) =>
        reader.GetInt64(reader.GetOrdinal(column));

    internal static DateTimeOffset GetTimestamp(this SqliteDataReader reader, string column) =>
        DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal(column)),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);

    internal static DateTimeOffset? GetNullableTimestamp(
        this SqliteDataReader reader,
        string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(index),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind);
    }

    internal static string ToStorage(this DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat);

    internal static object ToStorageOrNull(this DateTimeOffset? value) =>
        value is null ? DBNull.Value : value.Value.ToStorage();

    internal static object OrDbNull(this string? value) =>
        value is null ? DBNull.Value : value;
}
