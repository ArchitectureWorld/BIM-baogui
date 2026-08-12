from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "BIMBaoGui.RevitAddin"
WORKFLOW = ROOT / ".github" / "workflows" / "build-revit-mcp.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_native_addin_project_is_net48_wpf_and_has_no_gh_runtime_dependency():
    project = read(PROJECT / "BIMBaoGui.RevitAddin.csproj")
    assert "<TargetFramework>net48</TargetFramework>" in project
    assert "<UseWPF>true</UseWPF>" in project
    assert "Revit_All_Main_Versions_API_x64" in project
    all_source = "\n".join(
        read(path) for path in PROJECT.rglob("*") if path.suffix in {".cs", ".csproj"}
    )
    for forbidden in ("Grasshopper", "RhinoCommon", "RhinoInside"):
        assert forbidden not in all_source


def test_native_addin_compiles_rulepack_from_the_shared_authoritative_database():
    project = read(PROJECT / "BIMBaoGui.RevitAddin.csproj")
    assert "specs', 'hbr-rules', 'v1', 'source', 'hbr_rule_source.v1.json" in project
    assert "tools', 'build_hbr_rulepack.py" in project
    assert "HBR_RulePack.hbrpack" in project
    assert "BIMBaoGui.RevitAddin.Resources.HBR_RulePack.hbrpack" in project


def test_revit_application_registers_ribbon_and_dockable_workspace():
    source = read(PROJECT / "App.cs")
    assert "IExternalApplication" in source
    assert "IExternalCommand" in source
    assert "CreateRibbonPanel" in source
    assert "PushButtonData" in source
    assert "RegisterDockablePane" in source
    assert "DockablePaneProviderData" in source


def test_modeless_workspace_uses_external_event_queue_for_revit_api_calls():
    source = read(PROJECT / "RevitExternalEventDispatcher.cs")
    assert "ConcurrentQueue<RevitRequest>" in source
    assert "IExternalEventHandler" in source
    assert "ExternalEvent.Create" in source
    assert ".Raise()" in source
    assert "RevitDocumentSnapshotService.Capture" in source


def test_embedded_rulepack_reader_verifies_magic_length_and_sha256():
    source = read(PROJECT / "RulePackageIdentityReader.cs")
    assert "ExpectedMagic" in source
    assert "payloadLength" in source
    assert "SHA256.Create" in source
    assert "RulePackageSha256" in source
    assert "JavaScriptSerializer" in source


def test_addin_manifest_registers_the_exact_application_entrypoint():
    manifest = read(ROOT / "installer" / "BIMBaoGui.RevitAddin.addin")
    assert '<AddIn Type="Application">' in manifest
    assert "<FullClassName>BIMBaoGui.RevitAddin.App</FullClassName>" in manifest
    assert "<AddInId>6F3EE836-2A54-43C1-8B90-C9D291E9A8F1</AddInId>" in manifest


def test_unified_ci_runs_repository_contracts_and_builds_the_native_addin():
    workflow = read(WORKFLOW)
    assert "Verify native and MCP contracts" in workflow
    assert "tests/test_revit_addin_scaffold_contract.py" in workflow
    assert "dotnet build src/BIMBaoGui.RevitAddin/BIMBaoGui.RevitAddin.csproj" in workflow
    assert "TreatWarningsAsErrors=true" in workflow


def test_shared_rulepack_real_build_dependency_is_restored_before_rule_tests():
    workflow = read(WORKFLOW)
    restore = "dotnet restore src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    rule_tests = "python -m pytest tests/test_hbr_rulepack_compiler.py tests/test_hbr_rules_manifest.py -q"
    assert restore in workflow
    assert workflow.index(restore) < workflow.index(rule_tests)
