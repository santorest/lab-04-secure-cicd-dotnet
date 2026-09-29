using System.Globalization;
using System.Text.RegularExpressions;

namespace RepoPolicy.Tests;

public sealed record RegisterEntry(string Id, string Tool, string Rule, string Scope, string Reason, string Owner, DateOnly Expires);

/// <summary>Reads the table in security/EXCEPTIONS.md.</summary>
public static partial class ExceptionsRegister
{
    private static readonly string[] Columns = ["ID", "Tool", "Rule", "Scope", "Reason", "Owner", "Expires"];

    [GeneratedRegex(@"^\|\s*ID\s*\|\s*Tool\s*\|\s*Rule\s*\|\s*Scope\s*\|\s*Reason\s*\|\s*Owner\s*\|\s*Expires\s*\|\s*$")]
    private static partial Regex HeaderLine();

    /// <summary>Parses the first register table; throws <see cref="FormatException"/> naming the line on bad rows.</summary>
    public static IReadOnlyList<RegisterEntry> Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var header = Array.FindIndex(lines, l => HeaderLine().IsMatch(l.Trim()));
        if (header < 0)
        {
            throw new FormatException($"No register table found (header: | {string.Join(" | ", Columns)} |).");
        }

        var entries = new List<RegisterEntry>();
        // header + 1 is the |---| separator; rows follow until the first line that isn't a table row.
        for (var i = header + 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
        {
            var lineNumber = i + 1;
            var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != Columns.Length)
            {
                throw new FormatException($"line {lineNumber}: expected {Columns.Length} columns, found {cells.Length}.");
            }

            if (!DateOnly.TryParseExact(cells[6], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires))
            {
                throw new FormatException($"line {lineNumber}: Expires must be a date as yyyy-MM-dd, found '{cells[6]}'.");
            }

            entries.Add(new RegisterEntry(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], expires));
        }

        return entries;
    }
}
