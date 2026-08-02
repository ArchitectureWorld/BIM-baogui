from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]


def test_stage04_component_contract_is_unique_and_visible():
    component = ROOT / "src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs"
    text = component.read_text(encoding="utf-8")
    guid = "b43c4b26-80dc-4bb5-9171-5e2387bc7da2"

    assert "04 MVD IFC规范化" in text
    assert guid in text
    all_component_text = "\n".join(
        path.read_text(encoding="utf-8")
        for path in (ROOT / "src/BIMBaoGui.Stage01").glob("Stage*Component.cs")
    )
    assert len(re.findall(re.escape(guid), all_component_text, re.IGNORECASE)) == 1


def test_file_contract_refuses_overwrite_and_never_creates_backups():
    path_policy = (
        ROOT / "src/BIMBaoGui.Stage01/Mvd/MvdIfcPathPolicy.cs"
    ).read_text(encoding="utf-8")
    file_service = (
        ROOT / "src/BIMBaoGui.Stage01/Mvd/MvdIfcFileService.cs"
    ).read_text(encoding="utf-8")

    assert '"-MVD.ifc"' in path_policy
    assert "输出 IFC 不能覆盖源 IFC" in path_policy
    assert "File.Exists(destination)" in path_policy
    assert "File.Move(temporaryPath, destination)" in file_service
    assert ".bak" not in file_service.lower()
    assert ".backup" not in file_service.lower()


def test_failure_report_contract_uses_plugin_directory_prefix():
    writer = (
        ROOT / "src/BIMBaoGui.Stage01/Diagnostics/Stage04FailureReportWriter.cs"
    ).read_text(encoding="utf-8")

    assert "BIMBaoGui.Stage04.failure-" in writer
    assert "Path.GetDirectoryName(context.AssemblyPath)" in writer
    assert "DIAG_STAGE04_MVD_NORMALIZATION_FAILED" in writer


def test_release_docs_include_stage04_and_zero_backup_policy():
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    checklist = (
        ROOT / "docs/revit2020-v090-acceptance-checklist.md"
    ).read_text(encoding="utf-8")

    assert "04 MVD IFC 规范化" in readme
    assert "不创建插件备份" in readme
    assert "## Stage 04" in checklist
    assert "BIMBaoGui.Stage04.failure-" in checklist
