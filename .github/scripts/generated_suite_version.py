#!/usr/bin/env python3

import argparse
import re
import sys


RUN_ID_SUFFIX_PATTERN = re.compile(r"^.+-(\d+)$")


def normalize_provided_tag(version_tag: str, run_id: str) -> str:
    if not run_id.isdigit():
        raise ValueError("run_id must be numeric")

    normalized_tag = version_tag.rstrip("-")
    if not normalized_tag:
        raise ValueError("version_tag must include a non-empty prefix")

    if normalized_tag.endswith(f"-{run_id}"):
        normalized_tag = normalized_tag[: -(len(run_id) + 1)].rstrip("-")
        if not normalized_tag:
            raise ValueError("version_tag must include a non-empty prefix")
    elif RUN_ID_SUFFIX_PATTERN.fullmatch(normalized_tag):
        raise ValueError("version_tag must omit any existing workflow run ID suffix")

    return f"{normalized_tag}-{run_id}"


def extract_run_id(version_tag: str) -> str:
    match = RUN_ID_SUFFIX_PATTERN.fullmatch(version_tag)
    if not match:
        raise ValueError("version_tag must end with a hyphen-delimited numeric workflow run ID")

    return match.group(1)


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    normalize_parser = subparsers.add_parser("normalize-provided-tag")
    normalize_parser.add_argument("--version-tag", required=True)
    normalize_parser.add_argument("--run-id", required=True)

    extract_parser = subparsers.add_parser("extract-run-id")
    extract_parser.add_argument("--version-tag", required=True)

    args = parser.parse_args()

    try:
        if args.command == "normalize-provided-tag":
            print(normalize_provided_tag(args.version_tag, args.run_id))
        else:
            print(extract_run_id(args.version_tag))
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
