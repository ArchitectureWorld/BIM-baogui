import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".deps"))
import ifcopenshell
import ifcopenshell.guid

class CompareCase(unittest.TestCase):
    def test_missing_type_pset_is_found_without_export_receipt(self):
        path = ROOT / "tools/compare_case_ifc.py"
        self.assertTrue(path.exists(), "Independent case comparison is missing")
        spec = importlib.util.spec_from_file_location("compare_case", path)
        tool = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(tool)
        with tempfile.TemporaryDirectory() as tmp:
            a, b = Path(tmp)/"reference.ifc", Path(tmp)/"output.ifc"
            m = ifcopenshell.file(schema="IFC4")
            slab = m.create_entity("IfcSlab", GlobalId=ifcopenshell.guid.new(), Tag="42")
            prop = m.create_entity("IfcPropertySingleValue", Name="构件类型", NominalValue=m.create_entity("IfcLabel","250"))
            ps = m.create_entity("IfcPropertySet", GlobalId=ifcopenshell.guid.new(), Name="Pset_总平构件信息属性集", HasProperties=[prop])
            typ = m.create_entity("IfcSlabType", GlobalId=ifcopenshell.guid.new(), HasPropertySets=[ps], PredefinedType="FLOOR")
            m.create_entity("IfcRelDefinesByType", GlobalId=ifcopenshell.guid.new(), RelatedObjects=[slab], RelatingType=typ)
            m.write(str(a)); m.write(str(b))
            self.assertTrue(tool.compare(a,b)["equivalent"])
            invalid = m.create_entity("IfcDirection", DirectionRatios=[1.,0.,0.,0.])
            m.write(str(b))
            self.assertFalse(tool.compare(a,b)["equivalent"], "Invalid output schema passed")
            m.remove(invalid)
            typ.HasPropertySets = None; m.write(str(b))
            result = tool.compare(a,b)
            self.assertFalse(result["equivalent"])
            self.assertEqual(result["differences"][0]["code"], "MISSING_PROPERTY")
