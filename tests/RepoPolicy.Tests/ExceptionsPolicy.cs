namespace RepoPolicy.Tests;

/// <summary>
/// Every silenced finding must be registered, every registered exception must still be in use, and no exception
/// may outlive its expiry date (it stays valid through that date, UTC).
/// </summary>
public static class ExceptionsPolicy
{
    public const string RegisterPath = "security/EXCEPTIONS.md";

    public static IReadOnlyList<string> Check(string root, DateOnly today)
    {
        var registerFile = Path.Combine(root, RegisterPath);
        if (!File.Exists(registerFile))
        {
            return [$"{RegisterPath} is missing."];
        }

        var entries = ExceptionsRegister.Parse(File.ReadAllText(registerFile));
        var suppressions = Suppressions.Find(root);
        static string Key(string tool, string rule) => $"{tool}\u0000{rule}".ToUpperInvariant();

        var registered = entries.Select(e => Key(e.Tool, e.Rule)).ToHashSet(StringComparer.Ordinal);
        var used = suppressions.Select(s => Key(s.Tool, s.Rule)).ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();
        problems.AddRange(suppressions
            .Where(s => !registered.Contains(Key(s.Tool, s.Rule)))
            .Select(s => $"{s.Tool} suppression '{s.Rule}' in {s.Source} is not in the register ({RegisterPath})."));
        problems.AddRange(entries
            .Where(e => !used.Contains(Key(e.Tool, e.Rule)))
            .Select(e => $"Register entry {e.Id} ({e.Tool} '{e.Rule}') has no matching suppression; remove it."));
        problems.AddRange(entries
            .Where(e => e.Expires < today)
            .Select(e => $"Register entry {e.Id} ({e.Tool} '{e.Rule}') expired on {e.Expires:yyyy-MM-dd}; fix the finding or renew with a reason."));
        return problems;
    }
}
