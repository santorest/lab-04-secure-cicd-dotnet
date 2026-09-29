---
title: "Secure CI/CD Pipeline for a .NET API"
id: "lab-04-secure-cicd-dotnet"
category: "DevSecOps & AppSec"
type: "Lab"
status: "completed"
date: "2026-09-29"
time_to_reproduce: "2–3 hours (fork, enable Actions, apply the ruleset)"
skills: [.NET, ASP.NET Core, GitHub Actions, CodeQL, Semgrep, gitleaks, Trivy, OWASP ZAP, Checkov, CycloneDX, cosign]
frameworks: [NIST SSDF (SP 800-218), OWASP SAMM, CIS Controls v8, SLSA]
repo: "https://github.com/santorest/lab-04-secure-cicd-dotnet"
bundle: "Published on the portfolio site with its SHA-256 checksum"
---

# Secure CI/CD Pipeline for a .NET API

> **TL;DR** — A small ASP.NET Core ticketing API and a GitHub Actions pipeline in which every pull request
> must pass seven security gates (tests, vulnerable dependencies, SAST, secrets, workflow and Dockerfile
> configuration, container CVEs, and an authenticated DAST scan) before a branch ruleset lets it merge, and
> every merge ships a signed container image with SBOMs and build provenance that anyone can verify.
> Six deliberately broken demo pull requests show each gate blocking a real problem.
> **Everything here ran for real on GitHub-hosted runners; all numbers come from those runs.**

| | |
|---|---|
| **Role played** | DevSecOps engineer building the delivery pipeline for a small API team |
| **Environment** | Public GitHub repository, GitHub-hosted Ubuntu runners, GitHub Container Registry |
| **Tools** | .NET 10, xUnit, CodeQL, Semgrep, gitleaks, Checkov, actionlint, zizmor, Trivy, OWASP ZAP, CycloneDX, cosign |
| **Deliverable** | API + tests, `ci.yml` / `codeql.yml` / `release.yml`, branch ruleset, exceptions register, demo PRs, results |

---

## 1. Problem

- **Context:** a small team ships an internal helpdesk API. Code review alone doesn't catch a vulnerable
  transitive package, a secret pasted into a config file, an injectable query or an outdated base image —
  and nobody can prove which build produced the image running in production.
- **Goals:**
    1. Block those problems on the pull request, automatically, before they reach `main`.
    2. Make exceptions explicit: every accepted finding has a reason, an owner and an expiry date.
    3. Make every release verifiable: signed, with a bill of materials and build provenance.
- **Constraints:** free tooling only, no long-lived signing keys, no secrets available to pull-request jobs.

## 2. The API

A ticket API on ASP.NET Core 10 (minimal APIs, EF Core with SQLite):

- `POST /api/auth/login` issues a 15-minute JWT (HMAC-SHA256). Passwords are hashed with ASP.NET Core
  Identity; logins are rate-limited (5 per minute per IP) and accounts lock after 5 failures. Unknown users and
  wrong passwords get the same answer.
- Users see and create **their own** tickets; agents see all of them and move them through
  `open → in_progress → resolved → closed`, one step at a time. Another user's ticket answers **404**, not 403,
  so IDs can't be probed.
- Every response carries `Content-Security-Policy: default-src 'none'`, `X-Content-Type-Options`,
  `Referrer-Policy`, `Cross-Origin-Resource-Policy`, no `Server` header, and `Cache-Control: no-store` under
  `/api`. Errors are RFC 9457 problem details with no stack traces; bodies over 64 KB get 413.
- The container runs Microsoft's **chiseled** .NET image (no shell, no package manager) as a non-root user
  with a **read-only** root filesystem; only `/data` (the SQLite file) is writable.
- **60 .NET tests** (15 unit, 29 integration, 16 repo-policy), including forged, expired, `alg: none`,
  wrong-issuer and wrong-audience tokens, cross-user access, and bad paging input.

## 3. Pull-request gates

| Gate | Tool | Blocks when |
|---|---|---|
| build-test | `dotnet build` (warnings are errors) + xUnit | a build warning or a failing test |
| dependencies | NuGet audit during restore; Dependabot | a High/Critical advisory, direct or transitive |
| sast | Semgrep (`p/csharp`, `p/secrets`); CodeQL `security-extended` in its own workflow | a Semgrep ERROR finding; a High+ CodeQL alert (enforced by the ruleset) |
| secrets | gitleaks, full history of the commit under test | any finding |
| config | Checkov (Dockerfile, workflows), actionlint, zizmor | any finding |
| container | image build, read-only smoke test, Trivy | a fixable High/Critical CVE |
| dast | OWASP ZAP API scan, authenticated, driven by the OpenAPI document | a High alert, or a Medium on the fail list |

The pipeline protects itself too: every third-party action is pinned to a full commit SHA and every scanner
image to a digest (Dependabot proposes updates), workflows default to read-only permissions, checkouts don't
keep credentials, and pull-request jobs get no secrets — the DAST job generates its own throw-away signing key
and test user for each run.

**Branch ruleset on `main`:** changes only through pull requests; all 8 checks green and up to date; no
High+ CodeQL alerts; no force-push or deletion; linear history. It stores 0 required approvals because a single
maintainer can't approve their own pull request — a team would set 1+ approvals and CODEOWNERS review. A
direct `git push` to `main` is refused:

```
remote: error: GH013: Repository rule violations found for refs/heads/main.
remote: - Changes must be made through a pull request.
remote: - 8 of 8 required status checks are expected.
```

## 4. Exceptions process

A finding can be accepted instead of fixed only with a row in `security/EXCEPTIONS.md`: tool, rule, scope,
reason, owner and expiry date. A repo-policy test runs in `build-test` and fails the PR when a suppression
exists in any tool's own format (NuGet, Trivy, Semgrep, ZAP, Checkov, gitleaks) without a register row, when a
row has no suppression, or when a row has expired. The register currently has one entry: **EX-001**, Checkov
`CKV_DOCKER_2` (no `HEALTHCHECK`): the chiseled image has no shell or curl to run one, so `/health` is probed
from outside instead; it expires on 2027-03-31.

## 5. Release and verification

On every merge to `main`, `release.yml` pushes the image to GHCR tagged with the commit SHA, generates two
CycloneDX SBOMs (NuGet dependencies and the whole image), signs the image with **cosign keyless** signing
(GitHub's OIDC identity — no keys to store or leak), attaches the SBOM as a signed attestation, records
**build provenance**, and then verifies all of it the way an outsider would:

```bash
cosign verify ghcr.io/santorest/lab-04-secure-cicd-dotnet@<digest> \
  --certificate-identity-regexp '^https://github.com/santorest/lab-04-secure-cicd-dotnet/.github/workflows/release.yml@refs/heads/main$' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
gh attestation verify oci://ghcr.io/santorest/lab-04-secure-cicd-dotnet@<digest> --owner santorest
```

The first release was also verified from a separate Windows machine with cosign v3.1.3
([results/cosign-verify.txt](results/cosign-verify.txt)); the same check with another repository's identity
fails, as it should.

## 6. Results

All figures come from [results/results.json](results/results.json), generated from the public GitHub API by
`tools/collect_results.py` on 2026-09-29.

**Pipeline time** (successful runs on `main`):

| | Median | Runs |
|---|---|---|
| Whole PR pipeline (wall clock) | 275 s (4 min 35 s) | 5 |
| build-test | 32 s | 5 |
| dependencies | 27.5 s | 4 |
| sast (Semgrep) | 28.5 s | 4 |
| secrets | 11 s | 4 |
| config | 28.5 s | 4 |
| container | 66.5 s | 4 |
| dast (ZAP) | 167 s | 4 |
| Release (sign, SBOMs, provenance, verify) | 91 s | 4 |

**Demo pull requests** (each adds one deliberate problem; closed unmerged — details in
[docs/demo-prs.md](docs/demo-prs.md)):

| Demo | Deliberate problem | Stopped by |
|---|---|---|
| — | AWS-style key pair (random, never valid) | **GitHub push protection**, at `git push`, before CI |
| #7 | Generic API key in a config file | secrets (gitleaks) |
| #3 | `System.Text.Json` 8.0.4 (CVE-2024-43485, High) | dependencies (NuGet audit) — and build-test, CodeQL |
| #4 | SQL built by string concatenation | build-test: EF Core analyzer EF1003 |
| #8 | Same, with the analyzer warning silenced | CodeQL `cs/sql-injection` (High) + ZAP SQL Injection (High); PR **blocked** |
| #5 | Unpatched `aspnet:10.0.0-noble` base image | container: 15 fixable High CVEs (Trivy) |
| #6 | `Content-Security-Policy` header removed | build-test: integration test (DAST never needed) |

**Findings on the real code** and what happened to them:

| Finding | Tool | Outcome |
|---|---|---|
| `Cross-Origin-Resource-Policy` header missing (rule 90004) | ZAP | **Fixed**, with a test; the next scan passed 118/118 rules |
| No `HEALTHCHECK` in the Dockerfile (`CKV_DOCKER_2`) | Checkov | **Accepted** as EX-001, expires 2027-03-31 |
| 8 Medium CVEs in base-image OS packages | Trivy | Below the gate (not High/Critical); tracked in code scanning; Dependabot proposes base-image updates |
| gitleaks scanned every branch, so the demo key on one branch failed unrelated PRs | pipeline design | **Fixed** (PR #9): scan only the history of the commit under test |
| CodeQL, Semgrep, NuGet audit, gitleaks, actionlint, zizmor on the real code | — | 0 findings |

## 7. Lessons

- **Layers catch what single tools miss.** Semgrep's C# rules didn't flag the concatenated SQL and its secrets
  rules didn't flag the generic API key; the EF Core analyzer, CodeQL, ZAP and gitleaks did. No one tool would
  have caught all six demos.
- **Cheap checks run first.** Two demos never reached the expensive gates: a unit test and a compiler analyzer
  stopped them in the first 30 seconds.
- **Push protection is the first gate.** GitHub refused the AWS-style key before any CI ran.
- **Scanners need scoping.** A full-history secret scan that fetched every branch let one branch's leak fail
  everyone's PRs — found by a demo, fixed by scanning only the commit under test.
- **Workflows are code and need linting.** An unquoted colon in a step name and a wrongly formatted Checkov
  input both broke CI on the way; the config gate (actionlint, zizmor, Checkov) now checks every workflow change.
- **Pin, then check the pin.** The latest release of the Checkov GitHub Action still pulled Checkov 2.0.930;
  running the official image pinned by digest gave the current version.

## 8. Reproduce it

1. Fork the repository and enable Actions.
2. Apply the ruleset: `gh api --method POST repos/<you>/<repo>/rulesets --input .github/rulesets/main.json`.
3. Open a pull request: the seven CI gates and CodeQL run. Merge it: the release runs.
4. Verify the image with the commands in section 5 (replace the owner and repository).
5. Locally: .NET 10 SDK, `dotnet test`; collect results with `python tools/collect_results.py <owner/repo>`.

## 9. Mapping

| Control | Framework | Where |
|---|---|---|
| PW.7 Review and analyze code / PW.8 Test executable code | NIST SSDF | sast, CodeQL, build-test, dast |
| PS.2 Verify release integrity / PS.3 Archive and protect each release | NIST SSDF | cosign signature, provenance, SBOMs in GHCR |
| PO.3 Secure toolchains | NIST SSDF | pinned actions and images, least-privilege workflows, zizmor |
| Verification: Security Testing; Implementation: Secure Build | OWASP SAMM | gates, exceptions register |
| 16.4 / 16.12 Third-party components, code-level security checks | CIS Controls v8 | NuGet audit, Dependabot, SAST |
| Build L2 (signed, hosted-build provenance) | SLSA v1.0 | `release.yml` |
