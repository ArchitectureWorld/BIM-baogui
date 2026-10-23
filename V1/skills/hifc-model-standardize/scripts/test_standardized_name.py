import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from standardized_name import plan


class StandardizedNameTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.file("3D_歇马镇中心学校_综合楼.rvt")

    def tearDown(self):
        self.temp.cleanup()

    def file(self, name):
        path = self.root / name
        path.write_bytes(b"filename-only fixture, not a Revit model")
        return path

    def make(self, source=None, **kwargs):
        return plan(source or self.source, day="20261023", **kwargs)

    def test_building_name_uses_subject_once(self):
        result = self.make()
        self.assertEqual(Path(result["target"]).name,
                         "3D_歇马镇中心学校_报规_综合楼_20261023_V1.rvt")
        self.assertEqual(result["action"], "save_as")
        self.assertEqual(result["filename_kind_hint"], "单体建筑")

    def test_optional_3d_and_project_underscores(self):
        result = self.make(self.file("学校_一期_综合楼.rvt"))
        self.assertEqual(result["project"], "学校_一期")
        self.assertEqual(result["subject"], "综合楼")

    def test_site_is_recognized_before_date_version(self):
        result = self.make(self.file("3D_学校_报规_总图_20261022_V3.rvt"))
        self.assertEqual(result["filename_kind_hint"], "总图")
        self.assertEqual(result["action"], "reuse")
        self.assertEqual(result["date"], "20261022")
        self.assertEqual(result["ifc_stem"], "OUT_学校_报规_总图_20261023")

    def test_explicit_identity_for_legacy_filename(self):
        result = self.make(self.file("3D_学校-总图-20261022-02.rvt"),
                           project="学校", subject="总图")
        self.assertEqual(Path(result["target"]).name,
                         "3D_学校_报规_总图_20261023_V1.rvt")

    def test_ambiguous_legacy_filename_needs_identity(self):
        with self.assertRaises(ValueError):
            self.make(self.file("3D_学校-总图-20261022-02.rvt"))

    def test_old_date_suffix_not_guessed_as_building_name(self):
        with self.assertRaises(ValueError):
            self.make(self.file("3D_学校_总图_20261022.rvt"))

    def test_versions_follow_maximum_not_first_gap(self):
        for version in (1, 3, 12):
            self.file(f"3D_歇马镇中心学校_报规_综合楼_20261023_V{version}.rvt")
        self.assertTrue(self.make()["target"].endswith("_V13.rvt"))

    def test_windows_case_insensitive_collision(self):
        self.file("3D_歇马镇中心学校_报规_综合楼_20261023_v9.RVT")
        self.assertTrue(self.make()["target"].endswith("_V10.rvt"))

    def test_existing_standardized_model_is_reused(self):
        source = self.file("3D_学校_报规_综合楼_20261023_V2.rvt")
        result = self.make(source)
        self.assertEqual(result["target"], str(source.resolve()))
        self.assertEqual(result["action"], "reuse")

    def test_new_version_keeps_existing_identity(self):
        source = self.file("3D_学校_报规_综合楼_20261023_V2.rvt")
        result = self.make(source, new_version=True)
        self.assertTrue(result["target"].endswith("_V3.rvt"))
        self.assertEqual(result["action"], "save_as")

    def test_custom_destination_is_read_only(self):
        directory = self.root / "标准化成果"
        directory.mkdir()
        result = self.make(directory=directory)
        self.assertEqual(Path(result["target"]).parent, directory)
        self.assertEqual(list(directory.iterdir()), [])

    def test_reuse_does_not_hide_requested_identity_change(self):
        source = self.file("3D_学校_报规_综合楼_20261023_V2.rvt")
        with self.assertRaises(ValueError):
            self.make(source, subject="大门")

    def test_new_version_can_explicitly_rename_subject(self):
        source = self.file("3D_学校_报规_综合楼_20261023_V2.rvt")
        result = self.make(source, subject="大门", new_version=True)
        self.assertIn("_报规_大门_", result["target"])

    def test_unsafe_or_generic_components_rejected(self):
        for subject in ("单体", "单体建筑", "../总图", "总图.", "总:图", "CON"):
            with self.subTest(subject=subject), self.assertRaises(ValueError):
                self.make(project="学校", subject=subject)

    def test_invalid_or_nonplanning_standardized_names_rejected(self):
        for name in ("3D_学校_报规_总图_20260230_V1.rvt",
                     "3D_学校_施工图_总图_20261023_V1.rvt",
                     "3D_学校_报规_总图_20261023_V0.rvt"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.make(self.file(name))

    def test_old_standardized_prefix_is_read_but_not_emitted(self):
        source = self.file("标准化_学校_报规_综合楼_20261023_V2.rvt")
        result = self.make(source)
        self.assertEqual(result["action"], "save_as")
        self.assertTrue(Path(result["target"]).name.startswith("3D_学校_报规_综合楼_"))
        self.assertNotEqual(result["target"], str(source.resolve()))

    def test_invalid_date_rejected(self):
        with self.assertRaises(ValueError):
            plan(self.source, day="20260230")

    def test_ifc_stem_uses_release_policy_without_standardized_prefix(self):
        result = self.make()
        self.assertEqual(result["ifc_stem"],
                         "OUT_歇马镇中心学校_报规_综合楼_20261023")

    def test_standardized_building_does_not_leak_rvt_suffix_into_ifc(self):
        source = self.file("3D_学校_报规_综合楼_20261022_V7.rvt")
        result = self.make(source)
        self.assertEqual(result["action"], "reuse")
        self.assertEqual(result["ifc_stem"], "OUT_学校_报规_综合楼_20261023")

    def test_new_day_starts_new_rvt_sequence(self):
        source = self.file("3D_学校_报规_大门_20261022_V7.rvt")
        result = self.make(source, new_version=True)
        self.assertEqual(Path(result["target"]).name,
                         "3D_学校_报规_大门_20261023_V1.rvt")
        self.assertEqual(result["ifc_stem"], "OUT_学校_报规_大门_20261023")

    def test_underscores_in_explicit_identity_keep_field_order(self):
        result = self.make(project="学校_一期", subject="东区_教学楼")
        self.assertEqual(result["ifc_stem"],
                         "OUT_学校_一期_报规_东区_教学楼_20261023")

    def test_planning_never_creates_or_changes_a_model(self):
        before = {p.name: p.read_bytes() for p in self.root.iterdir()}
        result = self.make()
        self.assertFalse(result["writes_files"])
        self.assertFalse(Path(result["target"]).exists())
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.root.iterdir()})

    def test_cli_emits_parseable_plan(self):
        completed = subprocess.run([
            sys.executable, "-B", "-X", "utf8",
            str(Path(__file__).with_name("standardized_name.py")),
            "--source", str(self.source), "--date", "20261023"],
            text=True, encoding="utf-8", capture_output=True, check=True)
        self.assertEqual(json.loads(completed.stdout)["subject"], "综合楼")


if __name__ == "__main__":
    unittest.main()
