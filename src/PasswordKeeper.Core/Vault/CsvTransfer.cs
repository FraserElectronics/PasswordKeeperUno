using System.Text;

namespace PasswordKeeper.Core.Vault;

/// <summary>
/// CSV import/export (RFC 4180 quoting). Exported files are PLAINTEXT, so callers must warn the user.
/// Import accepts common column names used by other password managers.
/// </summary>
public static class CsvTransfer
{
    private static readonly string[] Columns = { "title", "url", "username", "password", "notes" };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title", ["name"] = "title",
        ["url"] = "url", ["login_uri"] = "url", ["website"] = "url", ["web site"] = "url",
        ["username"] = "username", ["login_username"] = "username", ["user"] = "username", ["login"] = "username",
        ["password"] = "password", ["login_password"] = "password",
        ["notes"] = "notes", ["note"] = "notes", ["extra"] = "notes", ["comments"] = "notes",
    };

    public static string Export(IEnumerable<VaultEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', Columns)).Append("\r\n");
        foreach (var e in entries)
        {
            sb.Append(Quote(e.Title)).Append(',')
              .Append(Quote(e.Url)).Append(',')
              .Append(Quote(e.Username)).Append(',')
              .Append(Quote(e.Password)).Append(',')
              .Append(Quote(e.Notes)).Append("\r\n");
        }
        return sb.ToString();
    }

    public static List<VaultEntry> Import(string csv)
    {
        var rows = Parse(csv);
        if (rows.Count == 0) return new();

        var map = new Dictionary<string, int>();
        for (var i = 0; i < rows[0].Count; i++)
        {
            if (Aliases.TryGetValue(rows[0][i].Trim(), out var canonical)) map.TryAdd(canonical, i);
        }
        if (!map.ContainsKey("title") && !map.ContainsKey("url"))
            throw new InvalidVaultFormatException("CSV needs a header row with at least a title or url column.");

        string Get(List<string> row, string col) =>
            map.TryGetValue(col, out var i) && i < row.Count ? row[i] : "";

        var result = new List<VaultEntry>();
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var title = Get(row, "title");
            var url = Get(row, "url");
            result.Add(new VaultEntry
            {
                Title = title.Length > 0 ? title : url,
                Url = url,
                Username = Get(row, "username"),
                Password = Get(row, "password"),
                Notes = Get(row, "notes"),
            });
        }
        return result;
    }

    private static string Quote(string value)
    {
        // Leading = + - @ can be executed as a formula when the CSV is opened in a spreadsheet.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var any = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; any = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); any = true; break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>(); any = false;
                    break;
                default: field.Append(c); any = true; break;
            }
        }
        if (any || field.Length > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        if (rows.Count > 0 && rows[0].Count > 0) rows[0][0] = rows[0][0].TrimStart('﻿');
        return rows;
    }
}
