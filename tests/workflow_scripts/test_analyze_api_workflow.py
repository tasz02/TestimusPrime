import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import unittest
from unittest.mock import patch


REPOSITORY = Path(__file__).resolve().parents[2]
WORKFLOW = (REPOSITORY / ".github" / "workflows" / "analyze-api.yml").read_text()


def workflow_step(name):
    marker = f"      - name: {name}\n"
    start = WORKFLOW.index(marker) + len(marker)
    following = re.search(r"(?m)^      - |^  [a-z][a-z-]*:", WORKFLOW[start:])
    end = start + following.start() if following else len(WORKFLOW)
    return WORKFLOW[start:end]


def inline_python(name):
    step = workflow_step(name)
    script = textwrap.dedent(step.split("        run: |\n", 1)[1])
    return compile(script.split("python <<'PY'\n", 1)[1].rsplit("\nPY", 1)[0], name, "exec")


VALIDATE = inline_python("Validate inputs and allocate fresh report directories")
STAGE = inline_python("Stage execution reports and generated suites separately")


class AnalyzeApiWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="workflow-tests-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.workspace = self.root / "consumer"
        self.workspace.mkdir()
        self.runner_temp = self.root / "runner-temp"
        self.runner_temp.mkdir()
        (self.workspace / ".github").mkdir()
        (self.workspace / ".github" / "testimusprime.json").write_text("{}")
        self.output = self.root / "outputs"
        self.summary = self.root / "summary"
        self.environment = {
            "GITHUB_WORKSPACE": str(self.workspace),
            "RUNNER_TEMP": str(self.runner_temp),
            "GITHUB_OUTPUT": str(self.output),
            "GITHUB_STEP_SUMMARY": str(self.summary),
            "GITHUB_RUN_ID": "123",
            "GITHUB_RUN_ATTEMPT": "2",
            "CONFIG_PATH": ".github/testimusprime.json",
            "TOOL_VERSION": "0.1.0",
            "RUN_MODE": "",
            "SUITE_TRIGGER": "",
            "API_START_COMMAND": "",
            "API_READY_URL": "",
        }

    def execute(self, code, **overrides):
        self.output.write_text("")
        with patch.dict(os.environ, {**self.environment, **overrides}):
            exec(code, {})
        return dict(line.split("=", 1) for line in self.output.read_text().splitlines())

    def stage(self, documents):
        source = self.root / "source"
        reports = self.root / "reports"
        suites = self.root / "suites"
        for directory in (source, reports, suites):
            directory.mkdir(exist_ok=True)
        for name, document in documents.items():
            (source / name).write_text(json.dumps(document))
        flags = self.execute(
            STAGE,
            REPORTS_DIRECTORY=str(source),
            ARTIFACT_DIRECTORY=str(reports),
            SUITE_ARTIFACT_DIRECTORY=str(suites),
        )
        return flags, sorted(path.name for path in reports.iterdir()), sorted(path.name for path in suites.iterdir())

    def test_config_path_resolves_inside_consumer_and_output_is_fresh(self):
        first = self.execute(VALIDATE)
        second = self.execute(VALIDATE)
        self.assertEqual(first["config"], str(self.workspace / ".github" / "testimusprime.json"))
        self.assertNotEqual(first["reports"], second["reports"])
        self.assertNotEqual(first["artifact-name"], second["artifact-name"])
        self.assertNotEqual(first["artifact-name"], first["suite-artifact-name"])
        for name in ("reports", "artifact", "suites"):
            path = Path(first[name])
            self.assertTrue(path.is_absolute())
            self.assertTrue(path.is_relative_to(self.runner_temp))
            self.assertTrue(path.is_dir())
            self.assertEqual(list(path.iterdir()), [])
            self.assertTrue(path.parent.name.startswith("testimusprime-run-"))
            self.assertFalse(any(part.startswith(".") for part in path.relative_to(self.runner_temp).parts))

    def test_publisher_checkout_and_upload_paths_use_visible_directories(self):
        publisher_paths = workflow_step("Allocate fresh publication directories")
        self.assertIn('tempfile.mkdtemp(prefix="testimusprime-publish-", dir=workspace)', publisher_paths)
        self.assertNotIn("include-hidden-files", WORKFLOW)

    def test_absolute_config_path_is_rejected_even_inside_checkout(self):
        with self.assertRaisesRegex(SystemExit, "inside the caller checkout"):
            self.execute(VALIDATE, CONFIG_PATH=str(self.workspace / ".github" / "testimusprime.json"))

    def test_config_path_cannot_escape_checkout(self):
        (self.root / "outside.json").write_text("{}")
        with self.assertRaisesRegex(SystemExit, "inside the caller checkout"):
            self.execute(VALIDATE, CONFIG_PATH="../outside.json")

    def test_config_symlink_cannot_escape_checkout(self):
        outside = self.root / "outside.json"
        outside.write_text("{}")
        (self.workspace / "linked.json").symlink_to(outside)
        with self.assertRaisesRegex(SystemExit, "inside the caller checkout"):
            self.execute(VALIDATE, CONFIG_PATH="linked.json")

    def test_config_must_exist_and_be_a_file(self):
        for value in ("missing.json", ".github"):
            with self.subTest(value=value), self.assertRaisesRegex(SystemExit, "inside the caller checkout"):
                self.execute(VALIDATE, CONFIG_PATH=value)

    def test_control_characters_in_config_path_are_rejected(self):
        (self.workspace / "config\ninjected.json").write_text("{}")
        with self.assertRaisesRegex(SystemExit, "Control characters"):
            self.execute(VALIDATE, CONFIG_PATH="config\ninjected.json")

    def test_exact_stable_and_prerelease_versions_are_accepted(self):
        for version in ("0.1.0", "1.2.3-preview.1"):
            with self.subTest(version=version):
                self.assertIn("reports", self.execute(VALIDATE, TOOL_VERSION=version))

    def test_floating_and_injected_tool_versions_are_rejected(self):
        for version in ("latest", "0.1.*", "0.1", "[0.1.0,1.0.0)", "0.1.0\ninjected=value", "$(echo 0.1.0)"):
            with self.subTest(version=version), self.assertRaisesRegex(SystemExit, "exact package version"):
                self.execute(VALIDATE, TOOL_VERSION=version)

    def test_supported_mode_and_trigger_overrides_are_accepted(self):
        for mode in ("", "Run", "Generate", "ExecuteGenerated"):
            with self.subTest(mode=mode):
                self.execute(VALIDATE, RUN_MODE=mode)
        for trigger in ("", "Commit", "PullRequest"):
            with self.subTest(trigger=trigger):
                self.execute(VALIDATE, SUITE_TRIGGER=trigger)

    def test_invalid_mode_and_trigger_overrides_are_rejected(self):
        with self.assertRaisesRegex(SystemExit, "Invalid mode"):
            self.execute(VALIDATE, RUN_MODE="PublishDashboard")
        with self.assertRaisesRegex(SystemExit, "Invalid trigger"):
            self.execute(VALIDATE, SUITE_TRIGGER="push")

    def test_api_start_requires_readiness_url(self):
        with self.assertRaisesRegex(SystemExit, "api-ready-url is required"):
            self.execute(VALIDATE, API_START_COMMAND="dotnet run")
        self.execute(VALIDATE, API_START_COMMAND="dotnet run", API_READY_URL="http://localhost/health")

    def test_generation_only_stages_suite_without_execution_ready_flag(self):
        flags, reports, suites = self.stage({
            "suite.json": {"versionTag": "testcases-v1", "testCases": [], "analysis": {}},
        })
        self.assertEqual(flags, {"ready": "false", "suites-ready": "true"})
        self.assertEqual(reports, [])
        self.assertEqual(suites, ["suite.json"])

    def test_failed_execution_report_is_staged_without_suite_flag(self):
        flags, reports, suites = self.stage({
            "run.json": {"runId": "run", "summary": {"failed": 1}, "results": []},
        })
        self.assertEqual(flags, {"ready": "true", "suites-ready": "false"})
        self.assertEqual(reports, ["run.json"])
        self.assertEqual(suites, [])

    def test_reports_and_suites_are_staged_separately(self):
        flags, reports, suites = self.stage({
            "run.json": {"runId": "run", "summary": {}, "results": []},
            "suite.json": {"versionTag": "v1", "testCases": [{"id": "test"}], "analysis": {}},
        })
        self.assertEqual(flags, {"ready": "true", "suites-ready": "true"})
        self.assertEqual(reports, ["run.json"])
        self.assertEqual(suites, ["suite.json"])

    def test_invalid_suite_schema_does_not_enable_upload(self):
        flags, reports, suites = self.stage({
            "missing-analysis.json": {"versionTag": "v1", "testCases": []},
            "numeric-version.json": {"versionTag": 1, "testCases": [], "analysis": {}},
            "blank-version.json": {"versionTag": " ", "testCases": [], "analysis": {}},
            "invalid-cases.json": {"versionTag": "v1", "testCases": [1], "analysis": {}},
            "invalid-analysis.json": {"versionTag": "v1", "testCases": [], "analysis": []},
            "array.json": [],
        })
        self.assertEqual(flags, {"ready": "false", "suites-ready": "false"})
        self.assertEqual(reports, [])
        self.assertEqual(suites, [])

    def test_malformed_json_symlinks_and_api_logs_are_not_uploaded(self):
        source = self.root / "source"
        source.mkdir()
        outside = self.root / "outside.json"
        outside.write_text(json.dumps({"versionTag": "v1", "testCases": [], "analysis": {}}))
        (source / "linked.json").symlink_to(outside)
        (source / "broken.json").write_text("{broken")
        (source / "api-start.log").write_text("not an artifact")
        flags, reports, suites = self.stage({})
        self.assertEqual(flags, {"ready": "false", "suites-ready": "false"})
        self.assertEqual(reports, [])
        self.assertEqual(suites, [])

    def test_uploads_always_run_but_publication_requires_execution_reports(self):
        report_upload = workflow_step("Upload reports even when tests fail (never API startup logs)")
        suite_upload = workflow_step("Upload generated suites even when a later step fails")
        self.assertIn("always() && steps.reports.outputs.ready == 'true'", report_upload)
        self.assertIn("always() && steps.reports.outputs.suites-ready == 'true'", suite_upload)
        self.assertIn("steps.paths.outputs.artifact-name", report_upload)
        self.assertIn("steps.paths.outputs.suite-artifact-name", suite_upload)
        self.assertIn("reports-ready: ${{ steps.upload.outcome == 'success' && steps.reports.outputs.ready == 'true' }}", WORKFLOW)
        publish = WORKFLOW.split("\n  publish:\n", 1)[1]
        self.assertIn("needs.analyze.outputs.reports-ready == 'true'", publish)
        self.assertNotIn("suites-ready", publish)

    def test_inline_shell_syntax_and_expression_isolation(self):
        scripts = re.findall(r"(?m)^        run: \|\n((?:^          .*\n|^\n)+)", WORKFLOW)
        self.assertTrue(scripts)
        for script in scripts:
            with self.subTest(script=script.splitlines()[0]):
                self.assertNotIn("${{", script)
                subprocess.run(["bash", "-n"], input=textwrap.dedent(script), text=True, check=True)


if __name__ == "__main__":
    unittest.main()
