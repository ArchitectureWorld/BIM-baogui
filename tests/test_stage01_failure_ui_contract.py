from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def test_read_only_and_deferred_fields_do_not_enter_user_input_validation():
    validation = read("src/BIMBaoGui.Stage01/Core/Stage01Validation.cs")
    input_rules = read("src/BIMBaoGui.Stage01/Core/FieldInputRules.cs")
    assert "definition.ReadOnly || definition.Deferred" in validation
    assert "definition.ReadOnly || definition.Deferred" in input_rules


def test_failed_commit_messages_are_visible_in_the_actionable_footer():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    feedback = read("src/BIMBaoGui.Stage01/Core/Stage01Feedback.cs")
    assert "OperationFailureMessages" in component
    assert "_owner.OperationFailureMessages" in attributes
    assert "最近写入：" in feedback
    assert "OperationFailureMessages.Count" in attributes


def test_enqueue_failure_is_recorded_as_a_real_failed_commit():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    assert "_lastCommit = new CommitResult" in component
    assert 'Status = "初始化失败"' in component


def test_footer_uses_loaded_assembly_version_not_stale_v050_literal():
    attributes = read("src/BIMBaoGui.Stage01/UI/Stage01ComponentAttributes.cs")
    assert "v0.5.0" not in attributes
    assert "BuildPluginVersionText" in attributes
    assert "Assembly.GetName().Version" in attributes


def test_patch_build_uses_v081_without_bumping_file_context_schema():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    versions = read("src/BIMBaoGui.Stage01/Context/HBRContextVersions.cs")
    workflow = read(".github/workflows/build-stage01-gha.yml")
    assert "<Version>0.8.1</Version>" in project
    assert 'public override string Version => "0.8.1"' in assembly
    assert 'FileContextSchema = "0.8.0"' in versions
    assert "0.8.1.0" in workflow
