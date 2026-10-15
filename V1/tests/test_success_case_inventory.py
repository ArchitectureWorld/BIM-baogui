import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".deps"))
import ifcopenshell
import ifcopenshell.guid

spec = importlib.util.spec_from_file_location("inventory_success_cases", ROOT / "tools/inventory_success_cases.py")
inventory = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inventory)


class SuccessCaseInventory(unittest.TestCase):
    def setUp(self):
        self.model = ifcopenshell.file(schema="IFC4")

    def root(self, kind, **args):
        return self.model.create_entity(kind, GlobalId=ifcopenshell.guid.new(), **args)

    def pset(self, name, props=None):
        if props is None:
            props = [self.model.create_entity("IfcPropertySingleValue", Name="名称", NominalValue=self.model.create_entity("IfcLabel", "案例"))]
        return self.root("IfcPropertySet", Name=name, HasProperties=props)

    def attach(self, owner, pset):
        return self.root("IfcRelDefinesByProperties", RelatedObjects=[owner], RelatingPropertyDefinition=pset)

    def test_shared_direct_pset_does_not_duplicate_owner(self):
        a, b = self.root("IfcSlab", Name="A"), self.root("IfcSlab", Name="B")
        pset = self.pset("Pset_绿地信息属性集")
        self.root("IfcRelDefinesByProperties", RelatedObjects=[a, b], RelatingPropertyDefinition=pset)
        self.attach(a, pset)
        report = inventory.inventory_model(self.model, {pset.Name})
        self.assertEqual(len(report["bindings"]), 2)
        self.assertEqual({r["owner_id"] for r in report["bindings"]}, {a.id(), b.id()})

    def test_type_definition_and_inheriting_occurrences_are_separate(self):
        pset = self.pset("Pset_窗信息属性集")
        kind = self.root("IfcWindowType", Name="窗型", HasPropertySets=[pset], PredefinedType="WINDOW", PartitioningType="NOTDEFINED")
        a, b = self.root("IfcWindow", Name="A"), self.root("IfcWindow", Name="B")
        self.root("IfcRelDefinesByType", RelatingType=kind, RelatedObjects=[a, b])
        report = inventory.inventory_model(self.model, {pset.Name})
        self.assertEqual(sum(r["origin"] == "type_definition" for r in report["bindings"]), 1)
        self.assertEqual({r["owner_id"] for r in report["bindings"] if r["origin"] == "type_inherited"}, {a.id(), b.id()})

    def test_quantity_and_unattached_pset_are_not_occurrence_requirements(self):
        slab = self.root("IfcSlab", Name="A")
        orphan = self.pset("Pset_未挂载信息属性集")
        q = self.model.create_entity("IfcQuantityArea", Name="面积", AreaValue=10.0)
        quantity = self.root("IfcElementQuantity", Name="Qto_SlabBaseQuantities", Quantities=[q])
        self.attach(slab, quantity)
        report = inventory.inventory_model(self.model, {orphan.Name})
        self.assertEqual(len(report["psets"]), 1)
        self.assertEqual(report["bindings"], [])
        self.assertEqual(report["orphan_pset_ids"], [orphan.id()])

    def test_business_and_general_type_psets_coexist_on_same_slab(self):
        slab = self.root("IfcSlab", Name="面积载体")
        direct = self.pset("Pset_建筑区域信息属性集")
        general = self.pset("Pset_总平构件信息属性集")
        self.attach(slab, direct)
        kind = self.root("IfcSlabType", Name="楼板型", HasPropertySets=[general], PredefinedType="FLOOR")
        self.root("IfcRelDefinesByType", RelatingType=kind, RelatedObjects=[slab])
        report = inventory.inventory_model(self.model, {direct.Name, general.Name})
        bindings = [r for r in report["bindings"] if r["owner_id"] == slab.id()]
        self.assertEqual({r["pset_name"] for r in bindings}, {direct.Name, general.Name})
        self.assertEqual({r["origin"] for r in bindings}, {"direct", "type_inherited"})

    def test_zero_false_and_missing_values_keep_their_types(self):
        props = [self.model.create_entity("IfcPropertySingleValue", Name=name, NominalValue=value) for name, value in [
            ("面积", self.model.create_entity("IfcReal", 0.0)),
            ("基底", self.model.create_entity("IfcBoolean", False)), ("备注", None)]]
        pset = self.pset("Pset_建筑楼层信息属性集", props)
        report = inventory.inventory_model(self.model, {pset.Name})
        self.assertEqual(len(report["psets"]), 1)
        values = report["psets"][0]["properties"]
        self.assertEqual([r["value"] for r in values], [0.0, False, None])
        self.assertEqual([r["nominal_type"] for r in values], ["IfcReal", "IfcBoolean", None])


if __name__ == "__main__":
    unittest.main()
