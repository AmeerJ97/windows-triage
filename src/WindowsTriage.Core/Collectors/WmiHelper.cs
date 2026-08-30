using System.Globalization;
using System.Management;

namespace WindowsTriage.Core.Collectors;

internal static class WmiHelper
{
    public static List<Dictionary<string, object?>> Query(string className, IReadOnlyList<string> properties, string nameSpace = @"root\cimv2", string? where = null)
    {
        if (properties.Count == 0) throw new ArgumentException("At least one WMI property must be requested.", nameof(properties));
        var projection = string.Join(", ", properties.Select(ValidateIdentifier));
        var query = string.IsNullOrWhiteSpace(where)
            ? $"SELECT {projection} FROM {ValidateIdentifier(className)}"
            : $"SELECT {projection} FROM {ValidateIdentifier(className)} WHERE {where}";
        using var searcher = new ManagementObjectSearcher(nameSpace, query);
        using var results = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementObject item in results)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties) row[property] = Normalize(item[property]);
            rows.Add(row);
        }
        return rows;
    }

    public static Dictionary<string, object?> FirstOrEmpty(string className, IReadOnlyList<string> properties, string nameSpace = @"root\cimv2", string? where = null)
        => Query(className, properties, nameSpace, where).FirstOrDefault() ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public static string? Text(this IReadOnlyDictionary<string, object?> row, string key) => row.GetValueOrDefault(key)?.ToString();
    public static bool? Bool(this IReadOnlyDictionary<string, object?> row, string key) => ConvertValue<bool>(row.GetValueOrDefault(key));
    public static ushort? UInt16(this IReadOnlyDictionary<string, object?> row, string key) => ConvertValue<ushort>(row.GetValueOrDefault(key));
    public static uint? UInt32(this IReadOnlyDictionary<string, object?> row, string key) => ConvertValue<uint>(row.GetValueOrDefault(key));
    public static ulong? UInt64(this IReadOnlyDictionary<string, object?> row, string key) => ConvertValue<ulong>(row.GetValueOrDefault(key));
    public static double? Double(this IReadOnlyDictionary<string, object?> row, string key) => ConvertValue<double>(row.GetValueOrDefault(key));
    public static IReadOnlyList<string> Strings(this IReadOnlyDictionary<string, object?> row, string key) => row.GetValueOrDefault(key) switch
    {
        IEnumerable<object?> values => values.Where(v => v is not null).Select(v => v!.ToString() ?? "").Where(v => v.Length > 0).ToArray(),
        string value when value.Length > 0 => [value],
        _ => Array.Empty<string>()
    };

    private static T? ConvertValue<T>(object? value) where T : struct
    {
        if (value is null) return null;
        try { return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static string ValidateIdentifier(string value)
    {
        if (value.Length == 0 || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_'))) throw new ArgumentException($"Invalid WMI identifier: {value}");
        return value;
    }

    private static object? Normalize(object? value)
    {
        if (value is string text && text.Length >= 14 && text.Take(14).All(char.IsDigit) && text.Contains('.'))
        {
            try { return ManagementDateTimeConverter.ToDateTime(text).ToString("O"); }
            catch { return text; }
        }
        return value is Array array ? array.Cast<object?>().Select(Normalize).ToList() : value;
    }
}
