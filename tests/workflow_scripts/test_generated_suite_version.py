import unittest

from pathlib import Path
import importlib.util


MODULE_PATH = Path(__file__).resolve().parents[2] / ".github" / "scripts" / "generated_suite_version.py"
SPEC = importlib.util.spec_from_file_location("generated_suite_version", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class GeneratedSuiteVersionTests(unittest.TestCase):
    def test_normalize_appends_run_id_suffix(self):
        self.assertEqual(
            MODULE.normalize_provided_tag("custom-suite", "123456"),
            "custom-suite-123456",
        )

    def test_normalize_preserves_existing_run_id_suffix(self):
        self.assertEqual(
            MODULE.normalize_provided_tag("custom-suite-123456", "123456"),
            "custom-suite-123456",
        )

    def test_normalize_collapses_duplicate_hyphens_before_run_id(self):
        self.assertEqual(
            MODULE.normalize_provided_tag("custom-suite--123456", "123456"),
            "custom-suite-123456",
        )

    def test_normalize_rewrites_numeric_only_tag_with_suffix(self):
        self.assertEqual(
            MODULE.normalize_provided_tag("123456", "123456"),
            "123456-123456",
        )

    def test_normalize_appends_current_run_id_to_numeric_suffix_prefix(self):
        self.assertEqual(
            MODULE.normalize_provided_tag("custom-suite-654321", "123456"),
            "custom-suite-654321-123456",
        )

    def test_normalize_rejects_empty_tag(self):
        with self.assertRaisesRegex(ValueError, "version_tag must include a non-empty prefix"):
            MODULE.normalize_provided_tag("", "123456")

    def test_normalize_rejects_trailing_hyphen_only_tag(self):
        with self.assertRaisesRegex(ValueError, "version_tag must include a non-empty prefix"):
            MODULE.normalize_provided_tag("-", "123456")

    def test_extract_run_id_returns_suffix(self):
        self.assertEqual(
            MODULE.extract_run_id("custom-suite-123456"),
            "123456",
        )

    def test_extract_run_id_rejects_missing_suffix(self):
        with self.assertRaisesRegex(ValueError, "version_tag must end with a hyphen-delimited numeric workflow run ID"):
            MODULE.extract_run_id("custom-suite")

    def test_extract_run_id_rejects_leading_hyphen_form(self):
        with self.assertRaisesRegex(ValueError, "version_tag must end with a hyphen-delimited numeric workflow run ID"):
            MODULE.extract_run_id("-123456")


if __name__ == "__main__":
    unittest.main()
