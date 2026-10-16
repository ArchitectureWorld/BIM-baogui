import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from contextlib import redirect_stdout, redirect_stderr
from io import StringIO

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".deps"))
import ifcopenshell
import ifcopenshell.guid

spec = importlib.util.spec_from_file_location("verify_ifc", ROOT / "tools/verify_ifc.py")
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


class VerifyNativeExport(unittest.TestCase):
    def fixture(self, *, wrong_owner=False, wrong_value=False, wrong_type=False, extra_pset=False, leaked_bridge=False):
        model = ifcopenshell.file(schema="IFC4")
        first = model.create_entity("IfcSlab", GlobalId=ifcopenshell.guid.new(), Name="A", Tag="1001")
        second = model.create_entity("IfcSlab", GlobalId=ifcopenshell.guid.new(), Name="B", Tag="1002")
        value = model.create_entity("IfcText", "40.5") if wrong_type else model.create_entity("IfcReal", 41.0 if wrong_value else 40.5)
        prop = model.create_entity("IfcPropertySingleValue", Name="投影面积", NominalValue=value)
        pset = model.create_entity("IfcPropertySet", GlobalId=ifcopenshell.guid.new(), Name="Pset_规划净用地信息属性集", HasProperties=[prop])
        model.create_entity("IfcRelDefinesByProperties", GlobalId=ifcopenshell.guid.new(), RelatedObjects=[second if wrong_owner else first], RelatingPropertyDefinition=pset)
        if extra_pset:
            other = model.create_entity("IfcPropertySet", GlobalId=ifcopenshell.guid.new(), Name="Pset_阳台信息属性集", HasProperties=[prop])
            model.create_entity("IfcRelDefinesByProperties", GlobalId=ifcopenshell.guid.new(), RelatedObjects=[first], RelatingPropertyDefinition=other)
        if leaked_bridge:
            bridge = model.create_entity("IfcPropertySingleValue", Name="__HIFCV1_probe", NominalValue=value)
            internal = model.create_entity("IfcPropertySet", GlobalId=ifcopenshell.guid.new(), Name="数据", HasProperties=[bridge])
            model.create_entity("IfcRelDefinesByProperties", GlobalId=ifcopenshell.guid.new(), RelatedObjects=[first], RelatingPropertyDefinition=internal)
        directory = Path(self.temp.name)
        path = directory / "test.ifc"
        model.write(str(path))
        receipt = {
            "doc_export_calls": 1, "ifc_postprocessed": False,
            "ifc_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "source_snapshot_before": "a" * 64, "source_snapshot_after": "a" * 64, "model_transaction": "RolledBack",
            "materialization": [{"element_id": 1001, "property_set": "Pset_规划净用地信息属性集", "property": "投影面积", "raw_type": "Real", "value": "40.5", "status": "MATERIALIZED"}],
        }
        receipt_path = directory / "test.receipt.json"
        receipt_path.write_text(json.dumps(receipt, ensure_ascii=False), encoding="utf-8")
        return path, receipt_path

    def setUp(self):
        base = ROOT / "validation/test-temp"
        base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base)

    def tearDown(self):
        self.temp.cleanup()

    def test_checks_real_value_type_and_owner(self):
        result = verify.verify_export(*self.fixture())
        self.assertTrue(result["all_passed"], result)
        self.assertEqual(result["verified_properties"], 1)

    def test_wrong_owner_fails(self):
        self.assertFalse(verify.verify_export(*self.fixture(wrong_owner=True))["all_passed"])

    def test_wrong_number_fails(self):
        self.assertFalse(verify.verify_export(*self.fixture(wrong_value=True))["all_passed"])

    def test_text_fallback_does_not_pass_as_real(self):
        self.assertFalse(verify.verify_export(*self.fixture(wrong_type=True))["all_passed"])

    def test_wrong_role_pset_fails(self):
        self.assertFalse(verify.verify_export(*self.fixture(extra_pset=True))["all_passed"])

    def test_temporary_bridge_must_not_leak_as_ifc_property(self):
        result = verify.verify_export(*self.fixture(leaked_bridge=True))
        self.assertFalse(result["all_passed"])
        self.assertIn("TEMPORARY_BRIDGE_LEAK", [x.get("code") for x in result["failures"]])

    def test_read_only_check_leaves_ifc_bytes_unchanged(self):
        path, receipt = self.fixture()
        before = path.read_bytes()
        verify.verify_export(path, receipt)
        self.assertEqual(path.read_bytes(), before)

    def test_missing_snapshot_evidence_fails(self):
        path, receipt_path = self.fixture()
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        receipt.pop("source_snapshot_before")
        receipt.pop("source_snapshot_after")
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        self.assertFalse(verify.verify_export(path, receipt_path)["all_passed"])

    def test_tampered_ifc_hash_fails(self):
        path, receipt_path = self.fixture()
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        receipt["ifc_sha256"] = "0" * 64
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        self.assertFalse(verify.verify_export(path, receipt_path)["all_passed"])

    def test_unknown_boolean_text_is_not_false(self):
        self.assertFalse(verify.same_value("not-a-bool", False, "Boolean"))

    def test_cli_refuses_to_replace_ifc_with_report(self):
        path, receipt = self.fixture()
        before = path.read_bytes()
        with patch("sys.argv", ["verify_ifc", str(path), "--receipt", str(receipt), "--output", str(path)]), redirect_stdout(StringIO()), redirect_stderr(StringIO()):
            with self.assertRaises(SystemExit) as raised:
                verify.main()
        self.assertEqual(raised.exception.code, 2)
        self.assertEqual(path.read_bytes(), before)

    def parking_fixture(self, *, wrong_entity=False, guid_override=False, missing_owner=False):
        model = ifcopenshell.file(schema="IFC4")
        export_id = "11111111222233334444555555555555"
        global_id = "1qCzKEnzX4z9jdKcdDoNjX" if guid_override else ifcopenshell.guid.compress(export_id)
        if wrong_entity:
            owner = model.create_entity("IfcSlab", GlobalId=global_id, Name="停车区域", Tag="1001")
        else:
            owner = model.create_entity("IfcSpace", GlobalId=global_id, Name="1", LongName="面积", CompositionType="ELEMENT", PredefinedType="SPACE")
        prop = model.create_entity("IfcPropertySingleValue", Name="车位数", NominalValue=model.create_entity("IfcInteger", 19))
        pset = model.create_entity("IfcPropertySet", GlobalId=ifcopenshell.guid.new(), Name="Pset_停车位信息属性集", HasProperties=[prop])
        model.create_entity("IfcRelDefinesByProperties", GlobalId=ifcopenshell.guid.new(), RelatedObjects=[owner], RelatingPropertyDefinition=pset)
        if missing_owner:
            owner.GlobalId = ifcopenshell.guid.new()
        directory = Path(self.temp.name)
        path = directory / "parking.ifc"
        model.write(str(path))
        receipt = {
            "doc_export_calls": 1, "ifc_postprocessed": False,
            "ifc_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "source_snapshot_before": "a" * 64, "source_snapshot_after": "a" * 64, "model_transaction": "RolledBack",
            "spatial_export_identities": [{"element_id": 1001, "unique_id": "source-area-1001", "ifc_entity": "IfcSpace", "export_id": export_id, "ifc_guid_override": global_id if guid_override else None}],
            "materialization": [{"element_id": 1001, "property_set": "Pset_停车位信息属性集", "property": "车位数", "raw_type": "Integer", "value": "19", "status": "MATERIALIZED"}],
        }
        receipt_path = path.with_suffix(".receipt.json")
        receipt_path.write_text(json.dumps(receipt, ensure_ascii=False), encoding="utf-8")
        return path, receipt_path

    def test_area_space_without_tag_uses_source_export_guid(self):
        result = verify.verify_export(*self.parking_fixture())
        self.assertTrue(result["all_passed"], result)
        self.assertEqual(result["verified_properties"], 1)

    def test_area_existing_ifc_guid_override_is_respected(self):
        result = verify.verify_export(*self.parking_fixture(guid_override=True))
        self.assertTrue(result["all_passed"], result)

    def test_parking_pset_on_slab_does_not_pass_as_area(self):
        result = verify.verify_export(*self.parking_fixture(wrong_entity=True))
        self.assertFalse(result["all_passed"])
        self.assertIn("OWNER_ENTITY_MISMATCH", [x.get("code") for x in result["failures"]])

    def test_area_owner_is_not_guessed_from_name_or_parking_pset(self):
        result = verify.verify_export(*self.parking_fixture(missing_owner=True))
        self.assertFalse(result["all_passed"])
        self.assertIn("OWNER_NOT_UNIQUE", [x.get("code") for x in result["failures"]])

    def test_invalid_area_export_identity_is_reported(self):
        path, receipt_path = self.parking_fixture()
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        receipt["spatial_export_identities"][0]["export_id"] = "invalid"
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        result = verify.verify_export(path, receipt_path)
        self.assertFalse(result["all_passed"])
        self.assertIn("INVALID_SPATIAL_EXPORT_IDENTITY", [x.get("code") for x in result["failures"]])


if __name__ == "__main__":
    unittest.main()
