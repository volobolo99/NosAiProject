import pytest

from scripts.build_function_index import generate


def test_generate_reports_parse_error_with_location():
    result = generate({"broken.ps1": "function {"}, "rev")
    coverage = result["coverage"][0]
    assert coverage["parse_errors"] > 0
    assert coverage["errors"][0]["line"] == 1
    assert coverage["errors"][0]["column"] >= 0
    with pytest.raises(ValueError, match="broken.ps1:1:"):
        generate({"broken.ps1": "function {"}, "rev", strict=True)


def test_generate_is_stably_sorted_and_carries_parser_metadata():
    sources = {
        "b.py": "def b():\n    return 1\n",
        "a.py": "def a():\n    return 1\n",
    }
    first = generate(sources, "rev")
    second = generate(dict(reversed(list(sources.items()))), "rev")
    assert first == second
    assert [item["path"] for item in first["coverage"]] == ["a.py", "b.py"]
    assert first["parser_versions"]["python"]


def test_strict_mode_accepts_clean_sources():
    result = generate({"clean.py": "def clean():\n    return True\n"}, "rev", strict=True)
    assert result["parse_errors"] == 0
