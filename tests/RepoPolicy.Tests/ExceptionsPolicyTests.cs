namespace RepoPolicy.Tests;

public sealed class ExceptionsPolicyTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private readonly string _root = Directory.CreateTempSubdirectory("repo-policy-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Register(params string[] rows) => Write("security/EXCEPTIONS.md",
        "| ID | Tool | Rule | Scope | Reason | Owner | Expires |\n|---|---|---|---|---|---|---|\n" + string.Concat(rows.Select(r => r + "\n")));

    [Fact]
    public void A_clean_repo_has_no_problems()
    {
        Register();
        Assert.Empty(ExceptionsPolicy.Check(_root, Today));
    }

    [Fact]
    public void Suppressions_are_found_in_every_tool_location()
    {
        Write("Directory.Build.props", "<Project><ItemGroup><NuGetAuditSuppress Include=\"https://github.com/advisories/GHSA-aaaa-bbbb-cccc\" /></ItemGroup></Project>");
        Write(".trivyignore.yaml", "vulnerabilities:\n  - id: CVE-2026-1111\n    statement: no fix\n");
        Write("src/App/Thing.cs", "var x = 1; // nosemgrep: csharp.lang.security.some-rule\n");
        Write("security/zap-rules.tsv", "# ZAP rules\n10038\tFAIL\t(CSP)\n10049\tIGNORE\t(Storable)\n10015\tWARN\t(Cache)\n");
        Write("Dockerfile", "# checkov:skip=CKV_DOCKER_2: no HEALTHCHECK in a chiseled image\nFROM scratch\n");
        Write(".github/workflows/ci.yml", "# checkov:skip=CKV_GHA_7: reason\n");
        Write(".gitleaksignore", "# comment\nabc123:src/file.cs:generic-api-key:10\n");

        var found = Suppressions.Find(_root).Select(s => $"{s.Tool} {s.Rule}").Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            [
                "checkov CKV_DOCKER_2", "checkov CKV_GHA_7", "gitleaks abc123:src/file.cs:generic-api-key:10",
                "nuget https://github.com/advisories/GHSA-aaaa-bbbb-cccc", "semgrep csharp.lang.security.some-rule",
                "trivy CVE-2026-1111", "zap 10015", "zap 10049",
            ],
            found);
    }

    public static TheoryData<string, string, string, string> Bypasses => new()
    {
        // path, content, expected tool, expected rule
        { "tests/Some.Tests/A.cs", "var x = 1; // nosemgrep\n", "semgrep", "*" },
        { "tools/script.py", "x = 1  # nosemgrep: python.lang.rule\n", "semgrep", "python.lang.rule" },
        { "src/App/B.cs", "var key = \"abc\"; // gitleaks:allow\n", "gitleaks", "allow:src/App/B.cs" },
        { ".gitleaks.toml", "[allowlist]\npaths = ['src']\n", "gitleaks", "config:.gitleaks.toml" },
        { ".semgrepignore", "src/\n", "semgrep", "config:.semgrepignore" },
        { ".checkov.yaml", "skip-check: [CKV_DOCKER_3]\n", "checkov", "config:.checkov.yaml" },
        { ".checkov.yml", "skip-check: [CKV_DOCKER_3]\n", "checkov", "config:.checkov.yml" },
        { "trivy.yaml", "scan:\n  skip-dirs: [app]\n", "trivy", "config:trivy.yaml" },
        { ".github/workflows/other.yaml", "# checkov:skip=CKV_GHA_1: x\n", "checkov", "CKV_GHA_1" },
        { "src/App/App.csproj", "<Project><PropertyGroup><NoWarn>$(NoWarn);NU1903</NoWarn></PropertyGroup></Project>", "nuget", "NoWarn:NU1903" },
        { "Directory.Packages.props", "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>", "nuget", "NuGetAudit:false" },
        { "src/App/App.csproj", "<ItemGroup><NuGetAuditSuppress Include=\"https://github.com/advisories/GHSA-x\" /></ItemGroup>", "nuget", "https://github.com/advisories/GHSA-x" },
        { "security/zap-rules.tsv", "40018\tOUTOFSCOPE\t.*search.*\n", "zap", "40018" },
    };

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Every_way_to_silence_a_scanner_counts_as_a_suppression(string path, string content, string tool, string rule)
    {
        Write(path, content);
        Assert.Contains(Suppressions.Find(_root), s => s.Tool == tool && s.Rule == rule);
    }

    [Fact]
    public void Build_output_and_git_folders_are_not_scanned()
    {
        Write("src/App/bin/Debug/x.cs", "// nosemgrep\n");
        Write("src/App/obj/y.cs", "// nosemgrep\n");
        Write(".git/z", "// gitleaks:allow\n");
        Assert.Empty(Suppressions.Find(_root));
    }

    [Fact]
    public void An_unregistered_suppression_is_reported()
    {
        Register();
        Write(".trivyignore.yaml", "vulnerabilities:\n  - id: CVE-2026-1111\n");
        var problem = Assert.Single(ExceptionsPolicy.Check(_root, Today));
        Assert.Contains("CVE-2026-1111", problem, StringComparison.Ordinal);
        Assert.Contains("not in the register", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_register_entry_without_a_suppression_is_reported()
    {
        Register("| EX-001 | trivy | CVE-2026-1111 | image | reason | @me | 2026-12-31 |");
        var problem = Assert.Single(ExceptionsPolicy.Check(_root, Today));
        Assert.Contains("EX-001", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_expired_entry_is_reported()
    {
        Register("| EX-001 | trivy | CVE-2026-1111 | image | reason | @me | 2026-09-30 |");
        Write(".trivyignore.yaml", "vulnerabilities:\n  - id: CVE-2026-1111\n");
        var problem = Assert.Single(ExceptionsPolicy.Check(_root, Today));
        Assert.Contains("expired", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_is_valid_through_its_expiry_date()
    {
        Register("| EX-001 | trivy | CVE-2026-1111 | image | reason | @me | 2026-10-01 |");
        Write(".trivyignore.yaml", "vulnerabilities:\n  - id: CVE-2026-1111\n");
        Assert.Empty(ExceptionsPolicy.Check(_root, Today));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Register("| EX-001 | NuGet | https://github.com/advisories/GHSA-AAAA-BBBB-CCCC | all | reason | @me | 2026-12-31 |");
        Write("Directory.Build.props", "<NuGetAuditSuppress Include=\"https://github.com/advisories/ghsa-aaaa-bbbb-cccc\" />");
        Assert.Empty(ExceptionsPolicy.Check(_root, Today));
    }

    [Fact]
    public void A_missing_register_is_reported() =>
        Assert.Contains(ExceptionsPolicy.Check(_root, Today), p => p.Contains("EXCEPTIONS.md", StringComparison.Ordinal));

    [Fact]
    public void This_repository_is_compliant()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "TicketApi.slnx")))
        {
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("TicketApi.slnx not found above the test directory.");
        }

        Assert.Empty(ExceptionsPolicy.Check(root, DateOnly.FromDateTime(DateTime.UtcNow)));
    }
}
