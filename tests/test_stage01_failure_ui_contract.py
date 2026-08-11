from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def test_read_only_and_deferred_fields_do_not_enter_user_input_validation():
    validation = read("src/BIMBaoGui.Stage01/Core/Stage01Validation.cs")
    input_rules = read("src/BIMBaoGui.Stage01/Core/FieldInputRules.cs")
    assert "definition.ReadOnly || definition.Deferred" in validation
    assert "definition.ReadOnly || definition.Deferred" in input_rules


def test_failed_commit_messages_are_merged_into_the_existing_actionable_footer():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    feedback = read("src/BIMBaoGui.Stage01/Core/Stage01Feedback.cs")
    assert "OperationFailureMessages" in component
    assert "MergeOperationFailureIntoSnapshot" in component
    assert "_snapshot.Messages =" in component
    assert "最近写入：" in feedback
    assert "IsWriteFailure" in feedback


def test_enqueue_failure_is_recorded_as_a_real_failed_commit():
    component = read("src/BIMBaoGui.Stage01/Stage01Component.cs")
    assert "_lastCommit = new CommitResult" in component
    assert 'Status = "初始化失败"' in component


def test_commit_exception_writes_json_report_and_keeps_ui_concise():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Stage01FailureReportWriter.TryWrite" in service
    assert "DIAG_STAGE01_COMMIT_FAILED" in service
    assert "exception.GetType().FullName" in service
    assert 'operationStage = "VALIDATION"' in service
    assert 'operationStage = "APPLY_UNITS"' in service
    assert 'operationStage = "PROJECT_POSITION"' in service
    assert 'operationStage = "PROJECT_INFORMATION"' in service
    assert 'operationStage = "INTERNAL_STORAGE"' in service
    assert 'operationStage = "OFFICIAL_PROJECTION"' in service
    assert 'operationStage = "TRANSACTION_COMMIT"' in service
    assert 'operationStage = "READBACK_VERIFICATION"' in service
    assert "错误报告=" in service
    assert "REPORT_WRITE_FAILED" in service
    assert "FormatExceptionChain" not in service


def test_v090_plugin_and_context_contract_versions_match():
    project = read("src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj")
    assembly = read("src/BIMBaoGui.Stage01/AssemblyInfo.cs")
    versions = read("src/BIMBaoGui.Stage01/Context/HBRContextVersions.cs")
    workflow = read(".github/workflows/build-stage01-gha.yml")
    assert "<Version>0.9.0</Version>" in project
    assert 'public override string Version => "0.9.0"' in assembly
    assert 'FileContextSchema = "0.9.0"' in versions
    assert "0.9.0.0" in workflow
