from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def test_v090_assembly_and_fixed_gha_name_are_explicit():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    stage01_ui = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    stage02_ui = read("src/BIMBaoGui.Stage01/UI/Stage02ComponentAttributes.cs")
    assert "<Version>0.9.0</Version>" in project
    assert "<FileVersion>0.9.0.0</FileVersion>" in project
    assert "<AssemblyVersion>0.9.0.0</AssemblyVersion>" in project
    assert 'public override string Version => "0.9.0"' in assembly
    assert "BIMBaoGui v0.9.0" in stage01_ui
    assert "Stage 02 v0.9.0" in stage02_ui
    assert "BIMBaoGui.Stage01.gha" in project


def test_readme_documents_real_deployment_path_and_all_three_stages():
    readme = read("README.md")
    assert r"%APPDATA%\Grasshopper\Libraries\BIMbaogui" in readme
    assert "BIMBaoGui.Stage01.gha" in readme
    assert "01 文件初始化" in readme
    assert "02 模型任务与骨架分流" in readme
    assert "03 官方 H-IFC 属性写入" in readme


def test_ci_packages_manifest_with_hash_and_commit_sha_under_concurrency():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    assert "concurrency:" in workflow
    assert "github.workflow" in workflow
    assert "github.ref" in workflow
    assert "cancel-in-progress: true" in workflow
    assert '0.9.0.0' in workflow
    assert "artifacts/BIMBaoGui.Stage01.gha" in workflow
    assert "artifacts/artifact-manifest.json" in workflow
    assert "Get-FileHash" in workflow
    assert "sha256" in workflow
    assert "commitSha" in workflow
    assert "github.sha" in workflow


def test_revit2020_v090_acceptance_checklist_covers_runtime_chain():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    assert "Stage 01" in checklist
    assert "Stage 02" in checklist
    assert "Stage 03" in checklist
    assert "false" in checklist and "true" in checklist
    assert "20260731test02-v090-validation.ifc" in checklist
    assert "SHA-256" in checklist
