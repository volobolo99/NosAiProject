#!/usr/bin/env python3
"""Build a deterministic, parser-backed function index for NosAiProject.

The index is a navigation aid for human and AI contributors. It deliberately
does not claim that every runtime/generated function is present. Parse
diagnostics are part of the output so a stale or malformed source cannot be
mistaken for complete coverage.
"""

from __future__ import annotations

import argparse
import fnmatch
import importlib
import importlib.metadata
import json
import pathlib
import re
import subprocess
from collections.abc import Iterable, Mapping
from typing import Any

from tree_sitter import Language, Parser


LANGS = {
    ".py": "python",
    ".cs": "c_sharp",
    ".js": "javascript",
    ".html": "javascript",
    ".cpp": "cpp",
    ".cc": "cpp",
    ".cxx": "cpp",
    ".h": "cpp",
    ".hpp": "cpp",
    ".sh": "bash",
    ".ps1": "powershell",
}

KINDS = {
    "function_definition",
    "function_declaration",
    "method_declaration",
    "constructor_declaration",
    "destructor_declaration",
    "operator_declaration",
    "conversion_operator_declaration",
    "local_function_statement",
    "lambda_expression",
    "anonymous_method_expression",
    "arrow_function",
    "function_expression",
    "method_definition",
    "accessor_declaration",
    "function_statement",
}

_EXCERPT_LIMIT = 160


def _package_version(language: str) -> str:
    """Return grammar package metadata without making it a hard dependency."""

    package = "tree-sitter-" + language.replace("_", "-")
    try:
        return importlib.metadata.version(package)
    except importlib.metadata.PackageNotFoundError:
        return "unknown"


def _get_parser(language: str, cache: dict[str, tuple[Parser, str]]) -> tuple[Parser, str]:
    if language in cache:
        return cache[language]
    module = importlib.import_module("tree_sitter_" + language)
    parser = Parser(Language(module.language()))
    value = (parser, _package_version(language))
    cache[language] = value
    return value


def _iter_sections(path: str, source: str) -> Iterable[tuple[str, int]]:
    """Yield source sections and their zero-based line offset."""

    if pathlib.Path(path).suffix.lower() != ".html":
        yield source, 0
        return
    found = False
    for match in re.finditer(r"<script\b[^>]*>(.*?)</script\s*>", source, re.IGNORECASE | re.DOTALL):
        found = True
        yield match.group(1), source[: match.start(1)].count("\n")
    if not found:
        yield "", 0


def _node_excerpt(raw: bytes, node: Any) -> str:
    value = raw[node.start_byte : node.end_byte].decode("utf-8", errors="replace")
    value = " ".join(value.split())
    return value[:_EXCERPT_LIMIT]


def _parse_errors(raw: bytes, root: Any) -> list[dict[str, Any]]:
    errors: list[dict[str, Any]] = []
    stack = [root]
    while stack:
        node = stack.pop()
        if node.type == "ERROR" or node.is_missing:
            point = node.start_point
            errors.append(
                {
                    "line": point.row + 1,
                    "column": point.column,
                    "node_type": node.type,
                    "missing": bool(node.is_missing),
                    "excerpt": _node_excerpt(raw, node),
                }
            )
        stack.extend(reversed(node.named_children))
    return sorted(errors, key=lambda item: (item["line"], item["column"], item["node_type"]))


def _function_rows(path: str, language: str, raw: bytes, tree: Any, line_offset: int) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    stack = [tree.root_node]
    while stack:
        node = stack.pop()
        kind = node.type
        if kind in KINDS or (language == "cpp" and kind == "function_declarator"):
            # A C++ function_definition contains a function_declarator; keep
            # the definition and avoid producing a duplicate declaration row.
            if language == "cpp" and kind == "function_declarator":
                parent = node.parent
                if parent is not None and parent.type == "function_definition":
                    stack.extend(reversed(node.named_children))
                    continue
            name = node.child_by_field_name("name") or node.child_by_field_name("declarator")
            body = node.child_by_field_name("body")
            end_byte = body.start_byte if body is not None else node.end_byte
            signature = raw[node.start_byte:end_byte].decode("utf-8", errors="replace")
            if kind in {"lambda_expression", "arrow_function", "anonymous_method_expression", "function_expression"}:
                signature = signature.split("=>", 1)[0] + " =>"
            parents: list[str] = []
            parent = node.parent
            while parent is not None:
                if parent.type in {
                    "class_declaration",
                    "struct_declaration",
                    "interface_declaration",
                    "record_declaration",
                    "namespace_declaration",
                    "class_definition",
                    "function_definition",
                }:
                    parent_name = parent.child_by_field_name("name")
                    if parent_name is not None:
                        parents.append(parent_name.text.decode("utf-8", errors="replace"))
                parent = parent.parent
            rows.append(
                {
                    "path": path,
                    "line": node.start_point.row + 1 + line_offset,
                    "kind": kind,
                    "name": name.text.decode("utf-8", errors="replace") if name else "<anonymous/accessor>",
                    "scope": ".".join(reversed(parents)),
                    "signature": " ".join(signature.split()),
                    "category": "third_party" if path.startswith(("third_party/", "tools/")) else "test" if path.startswith("tests/") else "project",
                }
            )
        stack.extend(reversed(node.named_children))
    return rows


def generate(
    sources: Mapping[str, str],
    revision: str,
    *,
    strict: bool = False,
    excluded_paths: Iterable[str] = (),
) -> dict[str, Any]:
    """Return a stable index and coverage diagnostics for ``sources``.

    ``strict=True`` is intended for CI and raises ``ValueError`` if any parser
    diagnostic is found. Exclusions are explicit glob patterns and are listed
    in the result so an AI worker can see what was intentionally omitted.
    """

    excluded = tuple(sorted({str(pattern) for pattern in excluded_paths if str(pattern).strip()}))
    parsers: dict[str, tuple[Parser, str]] = {}
    rows: list[dict[str, Any]] = []
    coverage: list[dict[str, Any]] = []
    parser_versions: dict[str, str] = {}
    all_errors: list[dict[str, Any]] = []

    for path, source in sorted(((str(path), str(value)) for path, value in sources.items()), key=lambda item: item[0]):
        if any(fnmatch.fnmatch(path, pattern) for pattern in excluded):
            continue
        suffix = pathlib.Path(path).suffix.lower()
        language = LANGS.get(suffix)
        if language is None:
            continue
        parser, version = _get_parser(language, parsers)
        parser_versions[language] = version
        file_errors: list[dict[str, Any]] = []
        function_count = 0
        for section, line_offset in _iter_sections(path, source):
            raw = section.encode("utf-8")
            tree = parser.parse(raw)
            section_errors = _parse_errors(raw, tree.root_node)
            for error in section_errors:
                error = dict(error)
                error["path"] = path
                error["line"] += line_offset
                file_errors.append(error)
            section_rows = _function_rows(path, language, raw, tree, line_offset)
            rows.extend(section_rows)
            function_count += len(section_rows)
        file_errors.sort(key=lambda item: (item["line"], item["column"], item["node_type"]))
        coverage.append(
            {
                "path": path,
                "language": language,
                "functions": function_count,
                "parse_errors": len(file_errors),
                "errors": file_errors,
            }
        )
        all_errors.extend(file_errors)

    rows.sort(key=lambda item: (item["path"], item["line"], item["kind"], item["name"], item["signature"]))
    coverage.sort(key=lambda item: item["path"])
    all_errors.sort(key=lambda item: (item["path"], item["line"], item["column"], item["node_type"]))
    result: dict[str, Any] = {
        "schema_version": "nosai.function_index.v2",
        "source_revision": str(revision),
        "coverage": coverage,
        "functions": rows,
        "parser_versions": parser_versions,
        "parse_errors": len(all_errors),
        "parse_error_details": all_errors,
        "excluded_paths": list(excluded),
        "limitations": [
            "Static syntax index, not a call graph; generated/runtime functions are absent.",
            "C++ headers are parsed as C++; preprocessor variants are not expanded.",
            "HTML inline scripts are indexed; external scripts must be indexed separately.",
            "Accessor and anonymous entries may lack a semantic name.",
            "Strict CI mode is required before declaring a snapshot complete.",
        ],
    }
    if strict and all_errors:
        locations = ", ".join(f"{item['path']}:{item['line']}:{item['column']}" for item in all_errors)
        raise ValueError("parse errors: " + locations)
    return result


def _load_sources(root: pathlib.Path) -> dict[str, str]:
    if root.is_file():
        payload = json.loads(root.read_text(encoding="utf-8"))
        if not isinstance(payload, dict):
            raise ValueError("source JSON must be an object mapping paths to text")
        return {str(path): str(value) for path, value in payload.items()}
    paths = subprocess.check_output(["git", "ls-files"], cwd=root, text=True).splitlines()
    sources: dict[str, str] = {}
    for path in paths:
        if pathlib.Path(path).suffix.lower() in LANGS:
            sources[path] = (root / path).read_text(encoding="utf-8-sig")
    return sources


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkout", type=pathlib.Path, help="Git checkout or JSON path-to-source fixture")
    parser.add_argument("revision", help="Git revision recorded in the index")
    parser.add_argument("output_directory", type=pathlib.Path)
    parser.add_argument("--strict", action="store_true", help="fail when any parser error is found")
    parser.add_argument("--exclude", action="append", default=[], help="glob path to exclude (repeatable)")
    args = parser.parse_args(argv)
    result = generate(_load_sources(args.checkout), args.revision, strict=args.strict, excluded_paths=args.exclude)
    args.output_directory.mkdir(parents=True, exist_ok=True)
    (args.output_directory / "FUNCTION_INDEX.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(json.dumps({"files": len(result["coverage"]), "functions": len(result["functions"]), "parse_errors": result["parse_errors"]}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

