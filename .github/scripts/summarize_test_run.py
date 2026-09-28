#!/usr/bin/env python3
"""Render markdown workflow summaries from TestimusPrime JSON run reports.

Smoke and regression workflows provide suite metadata such as suite name,
configured trigger, API base URL, and timeout. The generated-suite workflow
provides the version tag instead of suite/timeout details. Invalid report files
never fail summary generation; they are skipped and surfaced in the warnings
section so the workflow can still publish the best available summary.
"""

from __future__ import annotations

import argparse
import html
import json
from collections.abc import Mapping
from pathlib import Path
from urllib.parse import quote


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Render a markdown summary for a test run workflow.")
    parser.add_argument("--summary-title", required=True, help="Markdown heading text without the leading ##.")
    parser.add_argument("--report-directory", required=True, help="Directory containing report JSON files.")
    parser.add_argument("--execution-step-outcome", required=True, help="GitHub Actions outcome for the test execution step.")
    parser.add_argument("--dashboard-url", required=True, help="Dashboard URL to include in the summary.")
    parser.add_argument("--suite-name", help="Suite name to show in the summary.")
    parser.add_argument("--version-tag", help="Generated suite version tag to show in the summary.")
    parser.add_argument("--api-base-url", help="API base URL used by the run.")
    parser.add_argument("--trigger-event", help="Trigger event associated with the run.")
    parser.add_argument("--timeout-seconds", help="Per-request timeout used by the run.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report_directory = Path(args.report_directory)
    rows, totals, parse_errors = load_report_rows(report_directory, args.trigger_event)
    overall_pass_rate = round((totals["passed"] / totals["total"] * 100), 2) if totals["total"] else 0

    print(f"## {args.summary_title}")
    print()
    print("| Field | Value |")
    print("| --- | --- |")

    metadata_fields: list[tuple[str, object]] = []
    if args.suite_name:
        metadata_fields.append(("Suite", args.suite_name))
    if args.version_tag:
        metadata_fields.append(("Version tag", args.version_tag))
    if args.api_base_url:
        metadata_fields.append(("API base URL", args.api_base_url))
    if args.trigger_event:
        metadata_fields.append(("Trigger event", args.trigger_event))
    if args.timeout_seconds is not None:
        metadata_fields.append(("Timeout seconds", args.timeout_seconds))

    for label, value in metadata_fields:
        print(f"| {label} | `{escape_cell(value)}` |")

    print(f"| Execution step outcome | `{escape_cell(args.execution_step_outcome)}` |")
    print(f"| Report directory | `{escape_cell(report_directory)}` |")
    print(f"| Dashboard | [Open dashboard]({escape_link_destination(args.dashboard_url)}) |")
    print()
    print("### Summary totals")
    print()
    print("| Total tests | Passed | Failed | Pass rate | Reports |")
    print("| ---: | ---: | ---: | ---: | ---: |")
    print(f"| {totals['total']} | {totals['passed']} | {totals['failed']} | {overall_pass_rate:.2f}% | {len(rows)} |")
    print()
    print("### Test results")
    print()
    print("| Report | Run ID | Trigger | Completed | Total | Passed | Failed | Pass rate |")
    print("| --- | --- | --- | --- | ---: | ---: | ---: | ---: |")
    if rows:
        for row in rows:
            print(
                f"| `{escape_cell(row['report'])}` | "
                f"`{escape_cell(row['run_id'])}` | "
                f"`{escape_cell(row['trigger_event'])}` | "
                f"`{escape_cell(row['completed_at'])}` | "
                f"{row['total']} | {row['passed']} | {row['failed']} | {escape_cell(row['pass_rate'])} |"
            )
    else:
        print("| _No parsed reports found_ |  |  |  | 0 | 0 | 0 | 0.00% |")
    print()

    if parse_errors:
        print("### Report parsing warnings")
        print()
        for error in parse_errors:
            print(f"- {escape_cell(error)}")
        print()

    return 0


def load_report_rows(report_directory: Path, fallback_trigger_event: str | None) -> tuple[list[dict[str, object]], dict[str, int], list[str]]:
    rows: list[dict[str, object]] = []
    totals = {"total": 0, "passed": 0, "failed": 0}
    parse_errors: list[str] = []

    if report_directory.is_dir():
        report_files = sorted(report_directory.glob("*.json"))
    else:
        report_files = []
        parse_errors.append(f"Report directory not found: {report_directory}")

    for report_file in report_files:
        try:
            with report_file.open(encoding="utf-8") as handle:
                report = json.load(handle)
        except Exception as exc:  # summary generation must remain best-effort
            parse_errors.append(f"{report_file.name}: {exc}")
            continue

        if not isinstance(report, Mapping):
            parse_errors.append(f"{report_file.name}: expected a JSON object")
            continue

        summary = report.get("summary") or {}
        if not isinstance(summary, Mapping):
            summary = {}

        passed = as_int(summary.get("passed"))
        failed = as_int(summary.get("failed"))
        raw_total = summary.get("total")
        reported_total = as_int(raw_total)
        minimum_total = passed + failed
        total = reported_total if reported_total >= minimum_total else minimum_total
        if raw_total is not None and reported_total < minimum_total:
            parse_errors.append(
                f"{report_file.name}: summary total {raw_total!r} did not match passed + failed ({total}); using {total}."
            )
        pass_rate_text = f"{(passed / total * 100):.2f}%" if total else "0.00%"

        totals["total"] += total
        totals["passed"] += passed
        totals["failed"] += failed
        rows.append(
            {
                "report": report_file.name,
                "run_id": report.get("runId") or report_file.stem,
                "trigger_event": report.get("triggerEvent") or fallback_trigger_event or "",
                "completed_at": report.get("completedAt") or "",
                "total": total,
                "passed": passed,
                "failed": failed,
                "pass_rate": pass_rate_text,
            }
        )

    return rows, totals, parse_errors


def as_int(value: object) -> int:
    try:
        return max(int(value), 0)
    except (TypeError, ValueError):
        return 0


def escape_cell(value: object) -> str:
    text = "" if value is None else str(value)
    text = text.replace("|", "\\|")
    return html.escape(text, quote=False).replace("\n", "<br>")


def escape_link_destination(value: str) -> str:
    return quote(value, safe="/:#?&=@[]!$&'*+,;%-._~")


if __name__ == "__main__":
    raise SystemExit(main())
