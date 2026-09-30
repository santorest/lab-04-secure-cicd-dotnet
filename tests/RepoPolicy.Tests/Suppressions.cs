using System.Text.RegularExpressions;

namespace RepoPolicy.Tests;

public sealed record Suppression(string Tool, string Rule, string Source);

/// <summary>
/// Finds every way a scanner finding can be silenced in this repository: inline comments anywhere, each tool's
/// ignore/config files, and build properties that weaken the NuGet audit. Formats are read with regular
/// expressions (no YAML/XML libraries): the files are small and the patterns are fixed.
/// </summary>
public static partial class Suppressions
{
    // Folders never scanned: git internals, build output, and this project (its sources hold the patterns).
    private static readonly string[] SkippedDirectories = [".git", "bin", "obj", "TestResults", ".vs", "node_modules"];
    private const string SelfPath = "tests/RepoPolicy.Tests/";

    // Config files that make a scanner skip paths or rules wholesale. Their presence is a suppression.
    private static readonly (string File, string Tool)[] ConfigFiles =
    [
        (".gitleaks.toml", "gitleaks"), (".semgrepignore", "semgrep"), (".checkov.yaml", "checkov"),
        (".checkov.yml", "checkov"), ("trivy.yaml", "trivy"), ("trivy.yml", "trivy"),
    ];

    [GeneratedRegex(@"(?://|#)\s*nosemgrep\b(?::\s*([\w.\-]+))?")]
    private static partial Regex NoSemgrep();

    [GeneratedRegex(@"gitleaks:allow\b")]
    private static partial Regex GitleaksAllow();

    [GeneratedRegex(@"checkov:skip=([A-Z0-9_]+)")]
    private static partial Regex CheckovSkip();

    [GeneratedRegex(@"<NuGetAuditSuppress\s+Include=""([^""]+)""")]
    private static partial Regex NuGetSuppress();

    [GeneratedRegex(@"<NoWarn>([^<]*)</NoWarn>")]
    private static partial Regex NoWarn();

    [GeneratedRegex(@"\bNU19\d\d\b")]
    private static partial Regex NuGetAuditCode();

    [GeneratedRegex(@"<NuGetAudit>\s*false\s*</NuGetAudit>", RegexOptions.IgnoreCase)]
    private static partial Regex NuGetAuditOff();

    [GeneratedRegex(@"^\s*-\s*id:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex TrivyId();

    public static IReadOnlyList<Suppression> Find(string root)
    {
        var found = new List<Suppression>();
        foreach (var file in RepositoryFiles(root))
        {
            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            var name = Path.GetFileName(path);
            var text = File.ReadAllText(file);

            foreach (Match m in NoSemgrep().Matches(text))
            {
                // A bare "nosemgrep" silences every rule on the line.
                found.Add(new Suppression("semgrep", m.Groups[1].Success ? m.Groups[1].Value : "*", path));
            }

            if (GitleaksAllow().IsMatch(text))
            {
                found.Add(new Suppression("gitleaks", $"allow:{path}", path));
            }

            if (name == "Dockerfile" || (path.StartsWith(".github/workflows/", StringComparison.Ordinal) && name.EndsWith(".yml", StringComparison.Ordinal))
                || (path.StartsWith(".github/workflows/", StringComparison.Ordinal) && name.EndsWith(".yaml", StringComparison.Ordinal)))
            {
                found.AddRange(CheckovSkip().Matches(text).Select(m => new Suppression("checkov", m.Groups[1].Value, path)));
            }

            if (name.EndsWith(".csproj", StringComparison.Ordinal) || name.EndsWith(".props", StringComparison.Ordinal) || name.EndsWith(".targets", StringComparison.Ordinal))
            {
                found.AddRange(NuGetSuppress().Matches(text).Select(m => new Suppression("nuget", m.Groups[1].Value, path)));
                found.AddRange(NoWarn().Matches(text)
                    .SelectMany(m => NuGetAuditCode().Matches(m.Groups[1].Value))
                    .Select(code => new Suppression("nuget", $"NoWarn:{code.Value}", path)));
                if (NuGetAuditOff().IsMatch(text))
                {
                    found.Add(new Suppression("nuget", "NuGetAudit:false", path));
                }
            }

            if (path == ".trivyignore.yaml")
            {
                found.AddRange(TrivyId().Matches(text).Select(m => new Suppression("trivy", m.Groups[1].Value, path)));
            }

            // ZAP rules file: "<rule id>\t<action>\t<name or URL regex>"; anything but FAIL weakens the gate.
            if (path == "security/zap-rules.tsv")
            {
                found.AddRange(text.Split('\n')
                    .Where(l => l.Length > 0 && !l.StartsWith('#'))
                    .Select(l => l.TrimEnd('\r').Split('\t'))
                    .Where(f => f.Length >= 2 && f[1] is "IGNORE" or "WARN" or "OUTOFSCOPE")
                    .Select(f => new Suppression("zap", f[0].Trim(), path)));
            }

            // gitleaks: one finding fingerprint per line.
            if (path == ".gitleaksignore")
            {
                found.AddRange(text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith('#'))
                    .Select(l => new Suppression("gitleaks", l, path)));
            }

            foreach (var (configFile, tool) in ConfigFiles.Where(c => c.File == path))
            {
                found.Add(new Suppression(tool, $"config:{configFile}", path));
            }
        }

        return found;
    }

    private static IEnumerable<string> RepositoryFiles(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var relative = Path.GetRelativePath(root, sub).Replace('\\', '/') + "/";
                if (!SkippedDirectories.Contains(Path.GetFileName(sub)) && !relative.Equals(SelfPath, StringComparison.Ordinal))
                {
                    pending.Push(sub);
                }
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!IsBinary(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsBinary(string file) =>
        Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".gif" or ".ico" or ".zip" or ".gz" or ".dll" or ".exe" or ".pdb" or ".db";
}
