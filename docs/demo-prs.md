# Demonstration pull requests

Each demo PR adds one deliberate problem to otherwise clean code, to show which gate stops it. They are
titled `[demo] …`, were closed without merging, and their branches are kept. All results below are from the
real GitHub Actions runs on 2026-09-29; where the gate that fired differs from the one targeted, that is
recorded as it happened.

| PR | Deliberate problem | Targeted gate | Checks that failed | What actually caught it |
|---|---|---|---|---|
| — | Random, never-valid AWS-style key pair in `appsettings.Demo.json` | secrets | none (never reached CI) | **GitHub push protection** refused the `git push` itself (both the access key ID and the secret key). See [results/push-protection-aws-key.txt](../results/push-protection-aws-key.txt). |
| [#7](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/7) | Random generic API key (`api_key`) in `appsettings.Demo.json` | secrets | secrets | gitleaks rule `generic-api-key` (push protection doesn't cover generic keys; Semgrep `p/secrets` didn't flag it either). |
| [#3](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/3) | `System.Text.Json` 8.0.4 (High advisory CVE-2024-43485 / GHSA-8g4q-xg66-9fp4) | dependencies | dependencies, build-test, CodeQL | NuGet audit during restore (NU1903 as an error). Every job that restores fails, so the build and CodeQL fail too. |
| [#4](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/4) | Search endpoint building SQL by string concatenation (`FromSqlRaw`) | sast | build-test, CodeQL | The EF Core analyzer (EF1003) fails the build because warnings are errors. Semgrep `p/csharp` did **not** flag it; CodeQL couldn't analyze because the build failed first. |
| [#8](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/8) | Same as #4, with the analyzer silenced (`#pragma warning disable EF1003`) | sast | CodeQL, dast (and secrets, see note) | CodeQL alert `cs/sql-injection` (High) at `TicketEndpoints.cs:21`; the ruleset marked the PR **BLOCKED**. ZAP also found it at runtime: rule 40018 SQL Injection (High). |
| [#5](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/5) | Runtime image `aspnet:10.0.0-noble` (first .NET 10 release, unpatched) | container | container | Trivy: 15 fixable High (3 OS packages, e.g. gpgv CVE-2025-68973; 4 in the ASP.NET Core runtime, e.g. CVE-2026-26130; 8 in the .NET runtime, e.g. CVE-2026-26127), 0 Critical. |
| [#6](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/6) | `Content-Security-Policy` header removed | dast | build-test | An integration test asserts the header, so `build-test` failed and the container and DAST jobs never ran. ZAP rule 10038 is a FAIL rule as a second line. |

**Note on #8's secrets failure.** gitleaks flagged the dummy key from #7's branch, not from #8: the
checkout fetched every branch (`fetch-depth: 0`) and gitleaks scanned all refs, so one leaked branch failed
unrelated PRs. Fixed in [#9](https://github.com/santorest/lab-04-secure-cicd-dotnet/pull/9) by scanning only
the history of the commit under test (`--log-opts=HEAD`); after the fix, clean PRs pass and #7 still fails on
its own key.
