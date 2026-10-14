# Evidence

Archived copies of the CI runs and pull requests cited in the write-ups.

| Item | Date | Result | Artifacts | Cited claim |
| --- | --- | --- | --- | --- |
| pr 3 | 2026-09-29 | CLOSED |  | / #3 / `System.Text.Json` 8.0.4 (CVE-2024-43485, High) / dependencies (NuGet audit) — and build-test, CodeQL / |
| pr 4 | 2026-09-29 | CLOSED |  | / #4 / SQL built by string concatenation / build-test: EF Core analyzer EF1003 / |
| pr 5 | 2026-09-29 | CLOSED |  | / #5 / Unpatched `aspnet:10.0.0-noble` base image / container: 15 fixable High CVEs (Trivy) / |
| pr 6 | 2026-09-29 | CLOSED |  | / #6 / `Content-Security-Policy` header removed / build-test: integration test (DAST never needed) / |
| pr 7 | 2026-09-29 | CLOSED |  | / #7 / Generic API key in a config file / secrets (gitleaks) / |
| pr 8 | 2026-09-29 | CLOSED |  | / #8 / Same, with the analyzer warning silenced / CodeQL `cs/sql-injection` (High) + ZAP SQL Injection (High); PR **blocked** / |
| pr 9 | 2026-09-29 | MERGED |  | / gitleaks scanned every branch, so the demo key on one branch failed unrelated PRs / pipeline design / **Fixed** (PR #9): scan only the history of the commit… |
