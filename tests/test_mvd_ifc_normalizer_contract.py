from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]


def test_legacy_stage04_component_guid_is_unique_and_hidden():
    component = ROOT / "src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs"
    text = component.read_text(encoding="utf-8")
    guid = "b43c4b26-80dc-4bb5-9171-5e2387bc7da2"

    assert "04 MVD IFC规范化" in text
    assert guid in text
    assert "GH_Exposure.hidden" in text
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


def test_all_failure_report_allocators_use_exact_name_collision_hresult():
    writers = [
        ROOT / "src/BIMBaoGui.Stage01/Diagnostics/Stage01FailureReportWriter.cs",
        ROOT / "src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs",
        ROOT / "src/BIMBaoGui.Stage01/Diagnostics/Stage03FailureReportWriter.cs",
        ROOT / "src/BIMBaoGui.Stage01/Diagnostics/Stage04FailureReportWriter.cs",
    ]
    unsafe = []
    for path in writers:
        text = path.read_text(encoding="utf-8")
        if re.search(
            r"catch\s*\(IOException\)\s*when\s*\(\s*File\.Exists\(path\)\s*\)",
            text,
        ):
            unsafe.append(path.name)
        if "AtomicJsonReportWriter.IsCreateNewCollision(exception)" not in text:
            unsafe.append(path.name)

    assert not unsafe, f"unsafe collision filters: {sorted(set(unsafe))}"


def test_release_docs_publish_only_three_stages_and_zero_backup_policy():
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    checklist = (
        ROOT / "docs/revit2020-v090-acceptance-checklist.md"
    ).read_text(encoding="utf-8")

    for component in (
        "湖北BIM报规｜01 文件初始化",
        "湖北BIM报规｜02 构件与属性准备",
        "湖北BIM报规｜03 检测、导出与 H-IFC 转译",
    ):
        assert component in readme
        assert component in checklist

    assert "04 MVD IFC 规范化" not in readme
    assert "## Stage 04" not in checklist
    assert "BIMBaoGui.Stage04.failure-" not in checklist
    assert "只能保留一个 `BIMBaoGui.Stage01.gha`" in readme
    assert "直接覆盖固定名文件" in readme
    assert "0 个 `.bak` / `.backup`" in readme
    assert "活动目录只有一个 `BIMBaoGui.Stage01.gha`" in checklist
    assert "没有 `.bak` 或 `.backup` 插件备份" in checklist
