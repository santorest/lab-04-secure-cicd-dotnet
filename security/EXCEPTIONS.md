# Security exceptions register

A finding may be accepted instead of fixed only with an entry here. Every suppression in a tool's own
configuration must have a matching row (same tool and rule), and every row must match a suppression in use.
An entry is valid through its expiry date; after that CI fails until the finding is fixed or the exception is
renewed with a new reason. This is enforced by `tests/RepoPolicy.Tests` on every pull request.

What counts as a suppression (rule to register in brackets):

- NuGet: an audit-suppress item for an advisory URL [the URL], a NU19xx code in a NoWarn property
  [NoWarn:NU19xx], or turning the audit off [NuGetAudit:false], in any .csproj/.props/.targets file.
- Semgrep: an inline nosemgrep comment anywhere in the repo [the rule id, or * when none is named], or a
  .semgrepignore file [config:.semgrepignore].
- gitleaks: a line in .gitleaksignore [the fingerprint], an inline allow comment [allow:<file path>], or a
  .gitleaks.toml file [config:.gitleaks.toml].
- Trivy: an id in .trivyignore.yaml [the CVE], or a trivy.yaml/.yml config file [config:trivy.yaml].
- Checkov: a skip comment in the Dockerfile or a workflow [the check id], or a .checkov.yaml/.yml file.
- ZAP: an IGNORE, WARN or OUTOFSCOPE line in security/zap-rules.tsv [the rule id].

| ID | Tool | Rule | Scope | Reason | Owner | Expires |
|---|---|---|---|---|---|---|
| EX-001 | checkov | CKV_DOCKER_2 | Dockerfile (runtime stage) | Chiseled image has no shell or curl, so a Docker HEALTHCHECK can't run inside it; adding one would add attack surface. `/health` is probed from outside: the CI smoke test and the orchestrator's liveness probe. | @santorest | 2027-03-31 |
