"""Collect the Lab 04 results from the public GitHub API into JSON.

Usage: python tools/collect_results.py santorest/lab-04-secure-cicd-dotnet > results/results.json

Standard library only. Set GITHUB_TOKEN to raise the API rate limit (not needed for a public repo).
Every number in the write-up comes from this output, so it can be reproduced.
"""

from __future__ import annotations

import json
import os
import statistics
import sys
import urllib.request
from collections import defaultdict
from datetime import UTC, datetime

API = "https://api.github.com"


def fetch_json(url: str):
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json", "User-Agent": "lab-04-results"})
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request, timeout=30) as response:  # noqa: S310 - fixed https API host
        return json.load(response)


def _seconds(start: str, end: str) -> float:
    parse = lambda s: datetime.fromisoformat(s.replace("Z", "+00:00"))  # noqa: E731
    return (parse(end) - parse(start)).total_seconds()


def _successful_runs(repo: str, workflow: str, fetch) -> list[dict]:
    url = f"{API}/repos/{repo}/actions/workflows/{workflow}/runs?branch=main&status=success&per_page=100"
    return fetch(url)["workflow_runs"]


def collect(repo: str, fetch=fetch_json) -> dict:
    ci_runs = _successful_runs(repo, "ci.yml", fetch)
    durations: dict[str, list[float]] = defaultdict(list)
    for run in ci_runs:
        for job in fetch(run["jobs_url"])["jobs"]:
            if job["conclusion"] == "success":
                durations[job["name"]].append(_seconds(job["started_at"], job["completed_at"]))

    demos = []
    for pr in fetch(f"{API}/repos/{repo}/pulls?state=all&per_page=100"):
        if not pr["title"].startswith("[demo]"):
            continue
        checks = fetch(f"{API}/repos/{repo}/commits/{pr['head']['sha']}/check-runs?per_page=100")["check_runs"]
        failed = sorted({c["name"] for c in checks if c["conclusion"] == "failure"})
        demos.append({"number": pr["number"], "title": pr["title"], "url": pr["html_url"],
                      "failed_checks": failed, "merged": pr["merged_at"] is not None})

    releases = _successful_runs(repo, "release.yml", fetch)
    return {
        "repo": repo,
        "generated": datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "ci_runs": len(ci_runs),
        "pipeline_median_seconds": statistics.median(_seconds(r["run_started_at"], r["updated_at"]) for r in ci_runs)
        if ci_runs else None,
        "jobs": {name: {"median_seconds": statistics.median(values), "runs": len(values)}
                 for name, values in sorted(durations.items())},
        "demo_prs": sorted(demos, key=lambda d: d["number"]),
        "release": {
            "runs": len(releases),
            "median_seconds": statistics.median(_seconds(r["run_started_at"], r["updated_at"]) for r in releases)
            if releases else None,
            "latest_run_url": releases[0]["html_url"] if releases else None,
            "latest_head_sha": releases[0]["head_sha"] if releases else None,
        },
    }


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    json.dump(collect(argv[1]), sys.stdout, indent=2)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
