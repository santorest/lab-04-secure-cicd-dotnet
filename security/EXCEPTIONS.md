# Security exceptions register

A finding may be accepted instead of fixed only with an entry here. Every suppression in a tool's own
configuration must have a matching row (same tool and rule), and every row must match a suppression in use.
An entry is valid through its expiry date; after that CI fails until the finding is fixed or the exception is
renewed with a new reason. This is enforced by `tests/RepoPolicy.Tests` on every pull request.

Where suppressions live: `Directory.Build.props` (`NuGetAuditSuppress`, tool `nuget`), `.trivyignore.yaml`
(`trivy`), `// nosemgrep: <rule>` in `src/` (`semgrep`), `IGNORE`/`WARN` lines in `security/zap-rules.tsv`
(`zap`), `checkov:skip=<id>` in the Dockerfile or workflows (`checkov`), `.gitleaksignore` (`gitleaks`).

| ID | Tool | Rule | Scope | Reason | Owner | Expires |
|---|---|---|---|---|---|---|
