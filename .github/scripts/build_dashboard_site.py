#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build a static TestimusPrime dashboard site.")
    parser.add_argument("--reports-directory", action="append", required=True, help="Directory containing new report JSON files.")
    parser.add_argument("--existing-site", required=True, help="Existing Pages site directory to merge history from.")
    parser.add_argument("--output-site", required=True, help="Output directory for the rebuilt static site.")
    parser.add_argument("--assets-directory", required=True, help="Directory containing static dashboard assets.")
    parser.add_argument("--history-limit", type=int, default=20, help="Maximum number of runs to keep in published history.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report_directories = [Path(path).resolve() for path in args.reports_directory]
    existing_site = Path(args.existing_site).resolve()
    output_site = Path(args.output_site).resolve()
    assets_directory = Path(args.assets_directory).resolve()

    existing_runs = load_existing_runs(existing_site)
    new_runs = load_new_runs(report_directories)

    merged_runs = {run["runId"]: run for run in existing_runs}
    merged_runs.update({run["runId"]: run for run in new_runs})

    history_limit = max(args.history_limit, 1)
    kept_runs = sorted(
        merged_runs.values(),
        key=lambda run: parse_completed_at(run.get("completedAt")),
        reverse=True,
    )[:history_limit]

    reset_output_directory(output_site)
    shutil.copytree(assets_directory, output_site, dirs_exist_ok=True)
    data_directory = output_site / "data"
    per_run_directory = data_directory / "runs"
    per_run_directory.mkdir(parents=True, exist_ok=True)

    summary = build_summary(kept_runs)
    write_json(data_directory / "summary.json", summary)
    write_json(data_directory / "runs.json", kept_runs)

    for run in kept_runs:
        write_json(per_run_directory / f"{sanitize_run_id(run['runId'])}.json", run)

    (output_site / ".nojekyll").write_text("", encoding="utf-8")
    return 0


def load_existing_runs(existing_site: Path) -> list[dict]:
    per_run_directory = existing_site / "data" / "runs"
    if not per_run_directory.is_dir():
        return []

    return load_reports_from_files(sorted(per_run_directory.glob("*.json")))


def load_new_runs(report_directories: list[Path]) -> list[dict]:
    files: list[Path] = []
    for report_directory in report_directories:
        if report_directory.is_dir():
            files.extend(sorted(report_directory.glob("*.json")))
    return load_reports_from_files(files)


def load_reports_from_files(files: list[Path]) -> list[dict]:
    reports: list[dict] = []
    for file in files:
        with file.open(encoding="utf-8") as handle:
            payload = json.load(handle)
        if isinstance(payload, dict) and payload.get("runId"):
            reports.append(payload)
    return reports


def parse_completed_at(value: object) -> datetime:
    if not isinstance(value, str) or not value:
        return datetime.min.replace(tzinfo=timezone.utc)

    normalized = value.replace("Z", "+00:00")
    try:
        return datetime.fromisoformat(normalized)
    except ValueError:
        return datetime.min.replace(tzinfo=timezone.utc)


def build_summary(runs: list[dict]) -> dict:
    total_runs = len(runs)
    total_tests = sum(as_int(run.get("summary", {}).get("total")) for run in runs)
    passed_tests = sum(as_int(run.get("summary", {}).get("passed")) for run in runs)
    failed_tests = sum(as_int(run.get("summary", {}).get("failed")) for run in runs)
    average_pass_rate = round(
        sum(as_decimal(run.get("summary", {}).get("passRate")) for run in runs) / total_runs,
        2,
    ) if total_runs else 0

    recent_runs = [
        {
            "runId": run.get("runId"),
            "triggerEvent": run.get("triggerEvent"),
            "completedAt": run.get("completedAt"),
            "total": as_int(run.get("summary", {}).get("total")),
            "passed": as_int(run.get("summary", {}).get("passed")),
            "failed": as_int(run.get("summary", {}).get("failed")),
            "passRate": round(as_decimal(run.get("summary", {}).get("passRate")), 2),
        }
        for run in runs[:10]
    ]

    return {
        "totalRuns": total_runs,
        "totalTests": total_tests,
        "passedTests": passed_tests,
        "failedTests": failed_tests,
        "averagePassRate": average_pass_rate,
        "recentRuns": recent_runs,
    }


def as_int(value: object) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def as_decimal(value: object) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def sanitize_run_id(run_id: str) -> str:
    sanitized = re.sub(r"[^A-Za-z0-9._-]", "-", run_id).strip("-")
    if not sanitized:
        raise ValueError("Run ID must contain at least one valid filename character.")
    return sanitized


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def reset_output_directory(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


if __name__ == "__main__":
    raise SystemExit(main())
