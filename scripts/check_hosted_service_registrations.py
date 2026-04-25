#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


UNSAFE_FACTORY = re.compile(r"\bAddHostedService\s*\(\s*fun\b", re.MULTILINE)
UNSAFE_FUNC_FACTORY = re.compile(
    r"\bAddHostedService\s*\(\s*Func\s*<\s*IServiceProvider\s*,\s*IHostedService\s*>",
    re.MULTILINE,
)
UNSAFE_CAST = re.compile(r":>\s*IHostedService\b")


def is_source_file(path: Path) -> bool:
    if path.suffix not in {".fs", ".fsx", ".cs"}:
        return False

    ignored_parts = {"bin", "obj", ".git", "node_modules"}
    lower_parts = {part.lower() for part in path.parts}

    if lower_parts & ignored_parts:
        return False

    return "test" not in lower_parts and "tests" not in lower_parts


def line_number_for_offset(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def scan_file(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    failures: list[str] = []

    for pattern, message in [
        (
            UNSAFE_FACTORY,
            "Use AddHostedService<ConcreteHostedService>(fun sp -> ...) so DI records a concrete implementation type.",
        ),
        (
            UNSAFE_FUNC_FACTORY,
            "Use Func<IServiceProvider, ConcreteHostedService>; never register a factory typed as IHostedService.",
        ),
        (
            UNSAFE_CAST,
            "Do not cast hosted-service factories to IHostedService; it can make DI treat multiple services as identical.",
        ),
    ]:
        for match in pattern.finditer(text):
            failures.append(f"{path}:{line_number_for_offset(text, match.start())}: {message}")

    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description="Reject unsafe hosted-service DI registration patterns.")
    parser.add_argument("paths", nargs="*", default=["src"], help="Files or directories to scan.")
    args = parser.parse_args()

    roots = [Path(path) for path in args.paths]
    files: list[Path] = []

    for root in roots:
        if not root.exists():
            continue
        if root.is_file():
            files.append(root)
        else:
            files.extend(path for path in root.rglob("*") if path.is_file())

    failures = [
        failure
        for path in sorted(files)
        if is_source_file(path)
        for failure in scan_file(path)
    ]

    if failures:
        print("Unsafe hosted-service registrations found:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("Hosted-service registration check passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
