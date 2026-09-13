#!/usr/bin/env python3
"""Reject benchmark claims that lack reproducibility metadata or comparable inputs."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REQUIRED_FIELDS = (
    "commitSha", "sdkVersion", "worktreeState", "runtime", "operatingSystem", "processorCount",
    "benchmarkDotNetVersion", "configuration", "job", "corpusSha256",
    "rawArtifactDirectory", "requiredMetrics",
)
SHA256 = re.compile(r"^[0-9a-f]{64}$")


def load(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read metadata: {error}") from error


def validate(metadata: dict, expected_hash: str | None, required_runtime: str | None) -> list[str]:
    errors = [f"missing metadata field: {key}" for key in REQUIRED_FIELDS if not metadata.get(key)]
    commit = metadata.get("commitSha", "")
    if commit and not re.fullmatch(r"[0-9a-f]{7,64}", commit, re.IGNORECASE):
        errors.append("commitSha is not a Git SHA")
    if metadata.get("worktreeState") and metadata["worktreeState"] != "clean":
        errors.append("worktreeState is not clean")
    corpus = metadata.get("corpusSha256", "")
    if corpus and not SHA256.fullmatch(corpus):
        errors.append("corpusSha256 is not a SHA-256 digest")
    if expected_hash and corpus != expected_hash:
        errors.append("corpusSha256 differs from the expected corpus")
    if required_runtime and metadata.get("runtime") != required_runtime:
        errors.append("runtime differs from the required runtime")
    metrics = set(metadata.get("requiredMetrics", []))
    if "Allocated" not in metrics:
        errors.append("allocation metric is absent")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("metadata", type=Path)
    parser.add_argument("--compare", type=Path, help="metadata for the comparison baseline")
    parser.add_argument("--expected-corpus-sha256")
    parser.add_argument("--required-runtime")
    args = parser.parse_args()
    try:
        current = load(args.metadata)
        errors = validate(current, args.expected_corpus_sha256, args.required_runtime)
        if args.compare:
            baseline = load(args.compare)
            errors.extend(validate(baseline, args.expected_corpus_sha256, args.required_runtime))
            for key in ("corpusSha256", "runtime", "job", "configuration"):
                if current.get(key) != baseline.get(key):
                    errors.append(f"comparison mismatch for {key}")
    except ValueError as error:
        errors = [str(error)]
    if errors:
        for error in errors:
            print(f"REJECT: {error}", file=sys.stderr)
        return 1
    print("ACCEPT: benchmark metadata is complete and comparable")
    return 0


if __name__ == "__main__":
    sys.exit(main())
