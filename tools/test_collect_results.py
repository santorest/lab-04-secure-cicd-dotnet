"""Tests for collect_results.py (run: python -m unittest discover -s tools)."""

import unittest

from collect_results import API, collect

REPO = "owner/repo"


def job(name, start, end, conclusion="success"):
    return {"name": name, "conclusion": conclusion, "started_at": start, "completed_at": end}


def ci_run(run_id, start, end):
    return {"id": run_id, "run_started_at": start, "updated_at": end, "jobs_url": f"{API}/runs/{run_id}/jobs",
            "html_url": f"https://github.com/{REPO}/actions/runs/{run_id}", "head_sha": f"sha{run_id}"}


FIXTURES = {
    f"{API}/repos/{REPO}/actions/workflows/ci.yml/runs?branch=main&status=success&per_page=100": {"workflow_runs": [
        ci_run(1, "2026-09-29T10:00:00Z", "2026-09-29T10:05:00Z"),
        ci_run(2, "2026-09-29T11:00:00Z", "2026-09-29T11:07:00Z"),
        ci_run(3, "2026-09-29T12:00:00Z", "2026-09-29T12:06:00Z"),
        ci_run(4, "2026-09-29T09:00:00Z", "2026-09-29T09:00:27Z"),
    ]},
    f"{API}/runs/1/jobs": {"jobs": [
        job("build-test", "2026-09-29T10:00:00Z", "2026-09-29T10:00:30Z"),
        job("dast", "2026-09-29T10:02:00Z", "2026-09-29T10:04:30Z"),
    ]},
    f"{API}/runs/2/jobs": {"jobs": [
        job("build-test", "2026-09-29T11:00:00Z", "2026-09-29T11:00:40Z"),
        job("dast", "2026-09-29T11:02:00Z", "2026-09-29T11:05:00Z"),
        job("config", "2026-09-29T11:00:00Z", "2026-09-29T11:00:10Z", conclusion="skipped"),
    ]},
    f"{API}/runs/3/jobs": {"jobs": [
        job("build-test", "2026-09-29T12:00:00Z", "2026-09-29T12:00:20Z"),
        job("dast", "2026-09-29T12:02:00Z", "2026-09-29T12:04:00Z"),
    ]},
    f"{API}/runs/4/jobs": {"jobs": [
        job("build-test", "2026-09-29T09:00:00Z", "2026-09-29T09:00:25Z"),
    ]},
    f"{API}/repos/{REPO}/pulls?state=all&per_page=100": [
        {"number": 3, "title": "[demo] vulnerable package", "html_url": "u3", "head": {"sha": "d3"}, "merged_at": None},
        {"number": 2, "title": "Normal work", "html_url": "u2", "head": {"sha": "n2"}, "merged_at": "2026-09-29T10:00:00Z"},
    ],
    f"{API}/repos/{REPO}/commits/d3/check-runs?per_page=100": {"check_runs": [
        {"name": "dependencies", "conclusion": "failure"},
        {"name": "build-test", "conclusion": "failure"},
        {"name": "dependencies", "conclusion": "failure"},
        {"name": "container", "conclusion": "skipped"},
        {"name": "secrets", "conclusion": "success"},
    ]},
    f"{API}/repos/{REPO}/actions/workflows/release.yml/runs?branch=main&status=success&per_page=100": {"workflow_runs": [
        ci_run(9, "2026-09-29T13:00:00Z", "2026-09-29T13:01:40Z"),
    ]},
}


def fake_fetch(url):
    return FIXTURES[url]


class CollectTests(unittest.TestCase):
    def setUp(self):
        self.result = collect(REPO, fetch=fake_fetch)

    def test_counts_only_full_pipeline_runs(self):
        # Run 4 ran build-test only (before the other gates existed): it is not a full pipeline run.
        self.assertEqual(self.result["ci_runs"], 3)

    def test_job_medians_use_successful_jobs_only(self):
        jobs = self.result["jobs"]
        self.assertEqual(jobs["build-test"], {"median_seconds": 30.0, "runs": 3})
        self.assertEqual(jobs["dast"], {"median_seconds": 150.0, "runs": 3})
        self.assertNotIn("config", jobs)  # skipped jobs are left out

    def test_pipeline_median_is_wall_time_of_whole_runs(self):
        self.assertEqual(self.result["pipeline_median_seconds"], 360.0)

    def test_demo_prs_list_distinct_failed_checks(self):
        (demo,) = self.result["demo_prs"]
        self.assertEqual(demo["number"], 3)
        self.assertEqual(demo["failed_checks"], ["build-test", "dependencies"])
        self.assertFalse(demo["merged"])

    def test_latest_release(self):
        self.assertEqual(self.result["release"]["runs"], 1)
        self.assertEqual(self.result["release"]["median_seconds"], 100.0)
        self.assertTrue(self.result["release"]["latest_run_url"].endswith("/runs/9"))


if __name__ == "__main__":
    unittest.main()
