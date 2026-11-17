import copy
import io
import json
import unittest
from pathlib import Path
from unittest.mock import patch

from jsonschema import ValidationError
from validate_docs import (
    ROOT,
    load,
    validate,
    validate_example,
    validate_openapi,
    validate_relative_path,
    validate_log,
)


class ContractTests(unittest.TestCase):
    def test_repository_docs_and_examples(self):
        self.assertEqual(validate(ROOT / "docs")["httpEndpoints"], 0)

    def test_map_rejects_broken_indices_and_ragged_rows(self):
        value = load(ROOT / "docs/schemas/examples/leveldata.json")
        for rows in ([[0], [0, 0]], [[10]], [[-1]], [[0.0]], [[True]]):
            candidate = copy.deepcopy(value)
            candidate["mapData"]["map"] = rows
            with self.assertRaises((ValueError, ValidationError)):
                validate_example("leveldata", candidate, ROOT / "docs")

    def test_unknown_level_fields_survive_contract(self):
        value = load(ROOT / "docs/schemas/examples/leveldata.json")
        value["future"] = {"extension": True}
        validate_example("leveldata", value, ROOT / "docs")

    def test_project_rejects_duplicate_keys(self):
        value = load(ROOT / "docs/schemas/examples/arkmap.json")
        value["artMappings"].append(copy.deepcopy(value["artMappings"][0]))
        with self.assertRaises(ValueError):
            validate_example("arkmap", value, ROOT / "docs")

    def test_paths_reject_escape_and_windows_aliases(self):
        for value in (
            "../x",
            "x/../y",
            "C:/x",
            "x\\y",
            "a//b",
            "a/NUL.json",
            "a/b.",
            "a/b ",
        ):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_relative_path(value)
        validate_relative_path("maps/demo.json")

    def test_http_contract_does_not_invent_endpoints(self):
        value = load(ROOT / "docs/http/openapi.json")
        value["paths"] = {"/documents": {}}
        with self.assertRaises(ValueError):
            validate_openapi(value)

    def test_log_versions_are_reported_separately(self):
        schema = load(ROOT / "docs/schemas/diagnostic-event.schema.json")
        event = load(ROOT / "docs/schemas/examples/diagnostic-event.json")
        content = json.dumps(event) + '\n{"operation":"legacy"}\n\n'
        with (
            patch("validate_docs.load", return_value=schema),
            patch.object(Path, "open", return_value=io.StringIO(content)),
        ):
            self.assertEqual(
                validate_log(Path("events.jsonl"), ROOT / "docs"),
                {"validatedEvents": 1, "legacyEvents": 1},
            )

    def test_log_rejects_invalid_current_event(self):
        schema = load(ROOT / "docs/schemas/diagnostic-event.schema.json")
        with (
            patch("validate_docs.load", return_value=schema),
            patch.object(
                Path, "open", return_value=io.StringIO('{"schemaVersion":1}\n')
            ),
            self.assertRaises(ValidationError),
        ):
            validate_log(Path("events.jsonl"), ROOT / "docs")


if __name__ == "__main__":
    unittest.main()
