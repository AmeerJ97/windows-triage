using System.Management;

namespace WindowsTriage.Core.Collectors;

internal static class WmiHelper
{
    public static List<Dictionary<string, object?>> Query(string className, string nameSpace = @"root\cimv2", string? where = null)
    {
        var query = string.IsNullOrWhiteSpace(where)
            ? $"SELECT * FROM {className}"
            : $"SELECT * FROM {className} WHERE {where}";

        using var searcher = new ManagementObjectSearcher(nameSpace, query);
        using var results = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();

        foreach (ManagementObject item in results)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyData property in item.Properties)
            {
                row[property.Name] = Normalize(property.Value);
            }
            rows.Add(row);
        }

        return rows;
    }

    public static Dictionary<string, object?> FirstOrEmpty(string className, string nameSpace = @"root\cimv2", string? where = null)
    {
        return Query(className, nameSpace, where).FirstOrDefault()
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static object? Normalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text && LooksLikeWmiDate(text))
        {
            try
            {
                return ManagementDateTimeConverter.ToDateTime(text).ToString("O");
            }
            catch
            {
                return text;
            }
        }

        if (value is Array array)
        {
            var values = new List<object?>();
            foreach (var item in array)
            {
                values.Add(Normalize(item));
            }
            return values;
        }

        return value;
    }

    private static bool LooksLikeWmiDate(string value)
    {
        return value.Length >= 14
            && value.Take(14).All(char.IsDigit)
            && value.Contains('.');
    }
}
