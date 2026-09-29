using System.Text.RegularExpressions;

namespace RepoPolicy.Tests;

public sealed record Suppression(string Tool, string Rule, string Source);

/// <summary>
/// Finds every place a scanner finding is silenced, in each tool's own format. Formats are read with regular
/// expressions (no YAML/XML libraries): the files are small and the patterns are fixed.
/// </summary>
public static partial class Suppressions
{
    [GeneratedRegex(@"<NuGetAuditSuppress\s+Include=""([^""]+)""")]
    private static partial Regex NuGetSuppress();

    [GeneratedRegex(@"^\s*-\s*id:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex TrivyId();

    [GeneratedRegex(@"nosemgrep:\s*([\w.\-]+)")]
    private static partial Regex NoSemgrep();

    [GeneratedRegex(@"checkov:skip=([A-Z0-9_]+)")]
    private static partial Regex CheckovSkip();

    public static IReadOnlyList<Suppression> Find(string root)
    {
        var found = new List<Suppression>();

        void FromFile(string relativePath, string tool, Regex pattern)
        {
            var path = Path.Combine(root, relativePath);
            if (File.Exists(path))
            {
                found.AddRange(pattern.Matches(File.ReadAllText(path)).Select(m => new Suppression(tool, m.Groups[1].Value, relativePath)));
            }
        }

        FromFile("Directory.Build.props", "nuget", NuGetSuppress());
        FromFile(".trivyignore.yaml", "trivy", TrivyId());
        FromFile("Dockerfile", "checkov", CheckovSkip());

        var workflows = Path.Combine(root, ".github", "workflows");
        if (Directory.Exists(workflows))
        {
            foreach (var file in Directory.EnumerateFiles(workflows, "*.yml"))
            {
                FromFile(Path.GetRelativePath(root, file), "checkov", CheckovSkip());
            }
        }

        var src = Path.Combine(root, "src");
        if (Directory.Exists(src))
        {
            foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                FromFile(Path.GetRelativePath(root, file), "semgrep", NoSemgrep());
            }
        }

        // ZAP rules file: "<rule id>\t<IGNORE|WARN|FAIL>\t<name>"; IGNORE and WARN weaken the gate, FAIL doesn't.
        var zap = Path.Combine(root, "security", "zap-rules.tsv");
        if (File.Exists(zap))
        {
            found.AddRange(File.ReadLines(zap)
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => l.Split('\t'))
                .Where(f => f.Length >= 2 && f[1] is "IGNORE" or "WARN")
                .Select(f => new Suppression("zap", f[0].Trim(), "security/zap-rules.tsv")));
        }

        // gitleaks: one finding fingerprint per line.
        var gitleaks = Path.Combine(root, ".gitleaksignore");
        if (File.Exists(gitleaks))
        {
            found.AddRange(File.ReadLines(gitleaks)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => new Suppression("gitleaks", l, ".gitleaksignore")));
        }

        return found;
    }
}
