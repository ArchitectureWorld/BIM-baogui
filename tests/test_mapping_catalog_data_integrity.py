from collections import Counter
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
RULES_PATH = ROOT / "specs/hifc-mapping/v1/data/wuhan_planning_rules.v1.json"
BINDINGS_PATH = ROOT / "specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json"


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def duplicates(values):
    counts = Counter(value.casefold() for value in values if value and value.strip())
    return sorted(value for value, count in counts.items() if count > 1)


def test_mapping_catalog_source_data_can_be_loaded_without_alias_collisions():
    rules_doc = load(RULES_PATH)
    bindings_doc = load(BINDINGS_PATH)
    rules = rules_doc["properties"]
    bindings = bindings_doc["bindings"]

    assert len(rules) == 166
    assert len(bindings) == 166

    rule_ids = [item["propertyId"] for item in rules]
    binding_ids = [item["propertyId"] for item in bindings]
    binding_guids = [item["parameterGuid"] for item in bindings]
    binding_names = [item["parameterName"] for item in bindings]

    assert not duplicates(rule_ids), f"duplicate rule propertyId: {duplicates(rule_ids)}"
    assert not duplicates(binding_ids), f"duplicate binding propertyId: {duplicates(binding_ids)}"
    assert not duplicates(binding_guids), f"duplicate binding GUID: {duplicates(binding_guids)}"
    assert not duplicates(binding_names), f"duplicate parameterName: {duplicates(binding_names)}"

    rules_by_id = {item["propertyId"].casefold(): item for item in rules}
    missing = [item["propertyId"] for item in bindings if item["propertyId"].casefold() not in rules_by_id]
    assert not missing, f"bindings missing rules: {missing}"

    for item in bindings:
        assert item.get("parameterGuid")
        assert item.get("parameterName")
        assert item.get("category")
        rule = rules_by_id[item["propertyId"].casefold()]
        assert rule.get("official") is not None
        assert rule.get("canonical") is not None
        assert rule["official"].get("ifcEntity")
        assert rule["official"].get("propertySet")
        assert rule["official"].get("ifcProperty")
        assert rule["canonical"].get("sharedParameterType")
