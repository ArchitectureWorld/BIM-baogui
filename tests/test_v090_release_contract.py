import json
import os
from pathlib import Path
import re
import subprocess
import textwrap
import xml.etree.ElementTree as ET

import pytest


ROOT = Path(__file__).resolve().parents[1]
NUGET_PROJECT = "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
NUGET_INVENTORY_COMMAND = (
    f"dotnet list {NUGET_PROJECT} package --include-transitive --format json"
)
NUGET_VULNERABILITY_COMMAND = (
    f"dotnet list {NUGET_PROJECT} package --vulnerable "
    "--include-transitive --format json"
)


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def workflow_step(workflow: str, name: str) -> str:
    marker = f"      - name: {name}"
    assert marker in workflow, f"missing workflow step: {name}"
    start = workflow.index(marker)
    end = workflow.find("\n      - name:", start + len(marker))
    return workflow[start:] if end < 0 else workflow[start:end]


def workflow_steps_with_offsets(workflow: str) -> list[tuple[int, str]]:
    starts = [
        match.start()
        for match in re.finditer(r"(?m)^      - (?=\S)", workflow)
    ]
    assert starts, "workflow contains no YAML steps"
    return [
        (
            start,
            workflow[start : starts[index + 1]]
            if index + 1 < len(starts)
            else workflow[start:],
        )
        for index, start in enumerate(starts)
    ]


def workflow_step_run_commands(step: str) -> list[str]:
    lines = step.splitlines()
    commands: list[str] = []

    def append_powershell_run_block(block_lines: list[str]) -> None:
        continued_command = ""
        for line in block_lines:
            command = f"{continued_command} {line}".strip()
            if command.endswith("`"):
                continued_command = command[:-1].rstrip()
            else:
                commands.append(command)
                continued_command = ""
        assert not continued_command, "PowerShell run block has an unterminated continuation"

    index = 0
    while index < len(lines):
        match = re.match(r"^        run:\s*(.*?)\s*$", lines[index])
        if match is None:
            index += 1
            continue
        value = match.group(1)
        if value not in {"|", "|-", ">", ">-"}:
            if value:
                append_powershell_run_block([value])
            index += 1
            continue

        index += 1
        block_lines: list[str] = []
        while index < len(lines):
            line = lines[index]
            stripped = line.strip()
            indent = len(line) - len(line.lstrip(" "))
            if stripped and indent <= 8:
                break
            if stripped and indent > 8:
                block_lines.append(stripped)
            index += 1
        if value in {">", ">-"}:
            if block_lines:
                commands.append(" ".join(block_lines))
        else:
            append_powershell_run_block(block_lines)
    return commands


def assert_release_plugin_build_treats_warnings_as_errors(workflow: str) -> None:
    project_root = ET.fromstring(read(NUGET_PROJECT))

    def local_name(element: ET.Element) -> str:
        return element.tag.rsplit("}", 1)[-1]

    no_warn_settings = [
        (local_name(element), "".join(element.itertext()).strip())
        for element in project_root.iter()
        if local_name(element).casefold() == "nowarn"
    ]
    assert no_warn_settings == [("NoWarn", "1591")], (
        "the production project NoWarn baseline must be exactly 1591"
    )
    forbidden_build_properties = {
        "warningsnotaserrors",
        "msbuildwarningsasmessages",
        "msbuildwarningsnotaserrors",
        "warningsasmessages",
        "warninglevel",
        "codeanalysisruleset",
        "codeanalysistreatwarningsaserrors",
        "enablenetanalyzers",
        "runanalyzers",
        "runanalyzersduringbuild",
    }

    def is_forbidden_build_property(name: str) -> bool:
        folded_name = name.casefold()
        return (
            folded_name in forbidden_build_properties
            or folded_name.startswith("analysislevel")
            or folded_name.startswith("analysismode")
        )

    forbidden_project_settings = [
        local_name(element)
        for element in project_root.iter()
        if is_forbidden_build_property(local_name(element))
        and "".join(element.itertext()).strip()
    ]
    assert not forbidden_project_settings, (
        "the production project must not override warning or analyzer settings with "
        + ", ".join(forbidden_project_settings)
    )
    explicit_imports = [
        element
        for element in project_root.iter()
        if local_name(element).casefold() == "import"
    ]
    assert not explicit_imports, (
        "the production project must not use repository-controlled explicit imports"
    )
    analyzer_config_items = [
        local_name(element)
        for element in project_root.iter()
        if local_name(element).casefold()
        in {"editorconfigfiles", "globalanalyzerconfigfiles"}
    ]
    assert not analyzer_config_items, (
        "the production project must not inject analyzer config items with "
        + ", ".join(analyzer_config_items)
    )
    repository_files = [
        path
        for path in ROOT.rglob("*")
        if path.is_file()
        and ".git" not in {part.casefold() for part in path.parts}
    ]
    directory_build_files = sorted(
        path.relative_to(ROOT).as_posix()
        for path in repository_files
        if path.name.casefold()
        in {"directory.build.props", "directory.build.targets"}
    )
    assert not directory_build_files, (
        "repository-controlled Directory.Build files are forbidden: "
        + ", ".join(directory_build_files)
    )
    repository_diagnostic_configs = sorted(
        path.relative_to(ROOT).as_posix()
        for path in repository_files
        if path.name.casefold() == ".editorconfig"
        or path.suffix.casefold() in {".globalconfig", ".ruleset"}
    )
    assert not repository_diagnostic_configs, (
        "repository-controlled diagnostic configuration is forbidden: "
        + ", ".join(repository_diagnostic_configs)
    )

    required_build_input_paths = {
        "Directory.Build.*",
        "**/*.props",
        "**/*.targets",
        "**/.editorconfig",
        "**/*.globalconfig",
        "**/*.ruleset",
    }
    for event, next_event in (
        ("push", "pull_request"),
        ("pull_request", "workflow_dispatch"),
    ):
        event_marker = f"  {event}:"
        next_event_marker = f"  {next_event}:"
        assert workflow.count(event_marker) == 1
        event_start = workflow.index(event_marker)
        event_end = workflow.index(next_event_marker, event_start)
        event_block = workflow[event_start:event_end]
        paths_marker = "    paths:\n"
        assert event_block.count(paths_marker) == 1
        paths_start = event_block.index(paths_marker) + len(paths_marker)
        paths_block = event_block[paths_start:]
        configured_paths = {
            match.group(1).strip().strip("\"'")
            for line in paths_block.splitlines()
            if (match := re.match(r"^\s*-\s*(.+?)\s*$", line))
        }
        assert required_build_input_paths <= configured_paths, (
            f"{event} paths must cover every MSBuild props/targets input"
        )

    job_marker = "  build:\n"
    assert workflow.count(job_marker) == 1
    job_start = workflow.index(job_marker)
    next_job = re.search(
        r"(?m)^  [A-Za-z0-9_-]+\s*:\s*$",
        workflow[job_start + len(job_marker) :],
    )
    job_end = (
        len(workflow)
        if next_job is None
        else job_start + len(job_marker) + next_job.start()
    )
    build_job = workflow[job_start:job_end]
    for key in ("if", "continue-on-error"):
        assert not re.search(
            rf"(?mi)^    {re.escape(key)}\s*:",
            build_job,
        ), f"the release build job must not set {key}"

    steps = workflow_steps_with_offsets(workflow)
    project = "src/bimbaogui.stage01/bimbaogui.stage01.csproj"
    target_builds: list[tuple[int, str, str, str, list[str]]] = []
    for offset, step in steps:
        for run_command in workflow_step_run_commands(step):
            code = run_command.split("#", 1)[0].strip()
            for command in re.split(r"\s*(?:;|&&|\|\|)\s*", code):
                command = command.strip()
                raw_tokens = command.split()
                tokens = [
                    token.strip("\"'").replace("\\", "/").removeprefix("./").casefold()
                    for token in raw_tokens
                ]
                if tokens[:1] == ["&"]:
                    tokens = tokens[1:]
                if tokens[:1] == ["dotnet.exe"]:
                    tokens[0] = "dotnet"
                if tokens[:2] == ["dotnet", "--%"]:
                    tokens = tokens[:1] + tokens[2:]
                if (
                    len(tokens) >= 3
                    and tokens[0] == "dotnet"
                    and tokens[1] == "build"
                    and project in tokens
                ):
                    target_builds.append((offset, step, command, code, tokens))

    assert len(target_builds) == 1, (
        "the workflow must contain exactly one dotnet build for the production project"
    )
    build_offset, build_step, command, source_line, tokens = target_builds[0]
    assert not re.search(r"[;&|]", source_line)
    assert tokens.count(project) == 1
    assert tokens == [
        "dotnet",
        "build",
        project,
        "-c",
        "release",
        "--no-restore",
        "-p:continuousintegrationbuild=true",
        "-p:treatwarningsaserrors=true",
    ], "the production build command must use only the approved strict tokens"

    configurations: list[str] = []
    for index, token in enumerate(tokens):
        if token in {"-c", "--configuration"}:
            assert index + 1 < len(tokens)
            configurations.append(tokens[index + 1])
        elif token.startswith("--configuration="):
            configurations.append(token.split("=", 1)[1])
    assert configurations == ["release"]

    property_prefixes = (
        "-p:treatwarningsaserrors=",
        "/p:treatwarningsaserrors=",
    )
    property_values = [
        token.split("=", 1)[1]
        for token in tokens
        if token.startswith(property_prefixes)
    ]
    bare_switches = {
        "--warnaserror",
        "-warnaserror",
        "/warnaserror",
    }
    bare_warning_gates = [token for token in tokens if token in bare_switches]
    treats_warnings_as_errors = (
        property_values == ["true"] and not bare_warning_gates
    ) or (
        not property_values and len(bare_warning_gates) == 1
    )
    assert treats_warnings_as_errors, (
        "the actual Release plugin build must fail on every compiler warning"
    )
    assert not any(
        token.startswith(("-p:nowarn=", "/p:nowarn="))
        for token in tokens
    ), "the production build must not override NoWarn on the command line"

    def has_step_key(step: str, key: str) -> bool:
        return re.search(
            rf"(?mi)^        {re.escape(key)}\s*:",
            step,
        ) is not None

    for key in ("if", "continue-on-error"):
        assert not has_step_key(build_step, key), (
            f"the production build step must not set {key}"
        )

    chain: list[tuple[str, int, str]] = []
    for name in (
        "Verify compiled GHA",
        "Prepare validation artifact",
        "Upload GHA and manifest",
    ):
        marker = f"      - name: {name}"
        assert workflow.count(marker) == 1
        offset = workflow.index(marker)
        step = workflow_step(workflow, name)
        chain.append((name, offset, step))
    assert build_offset < chain[0][1] < chain[1][1] < chain[2][1]
    for name, _, step in chain:
        for key in ("if", "continue-on-error"):
            assert not has_step_key(step, key), (
                f"the {name} step must not set {key}"
            )

    verify_step = chain[0][2]
    prepare_step = chain[1][2]
    upload_step = chain[2][2]
    release_gha = (
        "src/BIMBaoGui.Stage01/bin/Release/net48/BIMBaoGui.Stage01.gha"
    )
    artifact_gha = "artifacts/BIMBaoGui.Stage01.gha"
    artifact_manifest = "artifacts/artifact-manifest.json"
    assert release_gha in verify_step
    assert release_gha in prepare_step
    assert artifact_gha in prepare_step
    assert artifact_manifest in prepare_step
    assert artifact_gha in upload_step
    assert artifact_manifest in upload_step

    upload_steps = [
        (offset, step)
        for offset, step in steps
        if re.search(
            r"(?mi)^        uses:\s*actions/upload-artifact@",
            step,
        )
    ]
    assert len(upload_steps) == 1
    assert upload_steps[0][0] == chain[2][1]


def run_nuget_gate(
    inventory_report: dict[str, object],
    vulnerability_report: dict[str, object],
) -> subprocess.CompletedProcess[str]:
    gate = workflow_step(
        read(".github/workflows/build-stage01-gha.yml"),
        "Fail on vulnerable NuGet packages",
    )
    marker = "        run: |\n"
    assert marker in gate
    script = textwrap.dedent(gate.split(marker, 1)[1])
    inventory_assignment = f"$inventoryJson = {NUGET_INVENTORY_COMMAND}"
    has_inventory_probe = inventory_assignment in script
    if has_inventory_probe:
        script = script.replace(
            inventory_assignment,
            "$inventoryJson = $env:HBR_NUGET_INVENTORY_JSON\n"
            "$global:LASTEXITCODE = 0",
            1,
        )
    vulnerability_assignment = (
        f"$reportJson = {NUGET_VULNERABILITY_COMMAND}"
    )
    assert vulnerability_assignment in script
    script = script.replace(
        vulnerability_assignment,
        "$reportJson = $env:HBR_NUGET_VULNERABILITY_JSON\n"
        "$global:LASTEXITCODE = 0",
        1,
    )
    environment = os.environ.copy()
    environment["HBR_NUGET_INVENTORY_JSON"] = json.dumps(inventory_report)
    environment["HBR_NUGET_VULNERABILITY_JSON"] = json.dumps(
        vulnerability_report if has_inventory_probe else inventory_report
    )
    return subprocess.run(
        [
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            script,
        ],
        cwd=ROOT,
        env=environment,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        timeout=30,
        check=False,
    )


def healthy_nuget_reports() -> tuple[dict[str, object], dict[str, object]]:
    project_path = (ROOT / NUGET_PROJECT).resolve().as_posix()
    inventory = {
        "version": 1,
        "projects": [
            {
                "path": project_path,
                "frameworks": [
                    {
                        "framework": "net48",
                        "topLevelPackages": [],
                        "transitivePackages": [],
                    }
                ],
            }
        ],
    }
    vulnerability = {
        "version": 1,
        "projects": [{"path": project_path}],
    }
    return inventory, vulnerability


def assert_task12_failure_report_rows(section: str, scenario: str) -> None:
    assert not re.search(
        r"^\| failure report (?:路径|SHA-256) \|",
        section,
        re.MULTILINE,
    ), f"{scenario} still has an ambiguous failure-report slot"
    for suffix in ("路径", "SHA-256"):
        stage02_row = re.search(
            rf"^\| Stage02 failure report {suffix} \| ([^|\n]*) \|  \|$",
            section,
            re.MULTILINE,
        )
        assert stage02_row, f"{scenario} missing Stage02 {suffix} rule"
        stage02_expected = stage02_row.group(1)
        assert "runId" not in stage02_expected
        assert "N/A" in stage02_expected
        for native_identity in (
            "reportId",
            "inputSignature",
            "fileGuid",
            "documentFingerprint",
            "packageId / version / hash",
            "occurredUtc / occurredLocal",
            "场景时间窗",
        ):
            assert native_identity in stage02_expected

        stage03_row = re.search(
            rf"^\| Stage03 failure report {suffix} \| ([^|\n]*) \|  \|$",
            section,
            re.MULTILINE,
        )
        assert stage03_row, f"{scenario} missing Stage03 {suffix} rule"
        stage03_expected = stage03_row.group(1)
        assert "runId" in stage03_expected
        assert "N/A" in stage03_expected
        if scenario == "FORCE_TECHNICAL_FATAL":
            fatal_expected = {
                "路径": (
                    "必须记录本场景 `runId` 的技术致命失败报告；"
                    "禁止填 `N/A`"
                ),
                "SHA-256": (
                    "必须记录本场景 `runId` 报告哈希；禁止填 `N/A`"
                ),
            }
            assert stage03_expected == fatal_expected[suffix]
            assert "允许填 `N/A`" not in stage03_expected


def assert_force_empty_reason_fields_evidence(section: str) -> None:
    required_note = (
        "本场景必须生成与本场景 `runId` 绑定的独立 fields JSON，并回填路径与 "
        "SHA-256。仅 fields JSON 报告写入本身发生技术失败时，才在“实际结果”"
        "和本场景 Stage03 failure report 槽说明偏差；不得将该证据标为“不适用”。"
    )
    required_rows = (
        "| fields JSON 路径 | 本场景独立路径；必须回填 |  |",
        "| fields JSON SHA-256 | 本场景文件哈希；必须回填 |  |",
    )
    assert required_note in section
    for row in required_rows:
        assert section.count(row) == 1
    for suffix in ("路径", "SHA-256"):
        row = re.search(
            rf"^\| fields JSON {suffix} \| ([^|\n]*) \|  \|$",
            section,
            re.MULTILINE,
        )
        assert row
        expected = row.group(1)
        assert "按实际回填" not in expected
        assert "N/A" not in expected


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


def test_readme_documents_real_deployment_path_and_exact_public_components():
    readme = read("README.md")
    assert r"%APPDATA%\Grasshopper\Libraries\BIMbaogui" in readme
    assert "BIMBaoGui.Stage01.gha" in readme
    assert "湖北BIM报规｜01 文件初始化" in readme
    assert "湖北BIM报规｜02 构件与属性准备" in readme
    assert "湖北BIM报规｜03 检测、导出与 H-IFC 转译" in readme
    assert "03 官方 H-IFC 属性写入" not in readme
    assert "04 MVD IFC 规范化" not in readme
    assert "Stage 04" not in readme


def test_readme_documents_current_three_stage_runtime_contract():
    readme = read("README.md")
    for required in (
        "Stage01 → Stage02 → Stage03",
        "既有项目身份",
        "坐标 X / Y",
        "高程",
        "真北",
        "Revit UI 可见、可编辑",
        "保存后持久",
        "缺构件",
        "名称不匹配",
        "缺参数",
        "空值",
        "未分类",
        "Autodesk Revit 标准 IFC4",
        "-RAW.ifc",
        "-HIFC-MVD.ifc",
        "-fields.json",
        "Strict",
        "Force",
        "非空原因",
        "技术致命",
        "单一规则包",
        "packageId",
        "version",
        "hash",
        "失败报告与活动 GHA 同目录",
        "RAW 不改写",
        "不覆盖已有目标",
    ):
        assert required in readme
    assert "官方 H-IFC 导出新的 IFC4" not in readme
    assert "只能保留一个 `BIMBaoGui.Stage01.gha`" in readme
    assert "0 个 `.bak` / `.backup`" in readme


def test_readme_python_validation_commands_are_portable_and_version_pinned():
    readme = read("README.md")
    workflow = read(".github/workflows/build-stage01-gha.yml")

    assert r"C:\ProgramData\Anaconda3\python.exe" not in readme
    assert "激活项目使用的 Python 环境后" in readme
    assert "Python 3.13" in readme
    assert (
        "python -m pip install --disable-pip-version-check "
        "pytest==8.3.5 jsonschema==4.23.0"
    ) in readme
    assert "python -m pytest -q" in readme
    assert "actions/setup-python" in workflow
    assert 'python-version: "3.13"' in workflow


def test_repository_text_eol_policy_is_minimal_and_lf():
    attributes_path = ROOT / ".gitattributes"
    assert attributes_path.is_file(), "missing repository EOL policy"
    attributes_bytes = attributes_path.read_bytes()
    assert b"\r" not in attributes_bytes
    expected = [
        "*.cs text eol=lf",
        "*.csproj text eol=lf",
        "*.json text eol=lf",
        "*.md text eol=lf",
        "*.py text eol=lf",
        "*.yml text eol=lf",
        "*.yaml text eol=lf",
    ]
    assert attributes_bytes.decode("utf-8").splitlines() == expected


def test_release_candidate_text_files_are_lf_only():
    candidates = (
        ".gitattributes",
        ".github/workflows/build-stage01-gha.yml",
        "README.md",
        "docs/revit2020-v090-acceptance-checklist.md",
        "specs/hifc-mapping/v1/README.md",
        "src/BIMBaoGui.Stage01/Context/HBRFileContext.cs",
        "src/BIMBaoGui.Stage01/Diagnostics/Stage02FailureReportWriter.cs",
        "src/BIMBaoGui.Stage01/Hifc/Stage01OfficialCompatibilityPolicy.cs",
        "src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs",
        "src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs",
        "src/BIMBaoGui.Stage01/Stage01Component.cs",
        "src/BIMBaoGui.Stage01/Stage02/Stage02PreparationInputPolicy.cs",
        "src/BIMBaoGui.Stage01/Stage02ElementPreparationComponent.cs",
        "src/BIMBaoGui.Stage01/TaskPlanning/TaskPlanCompiler.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/HBRFileContextTests.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/Stage01OfficialCompatibilityPolicyTests.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/Stage02FailureReportWriterTests.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreparationInputPolicyTests.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/Stage02PreviewCompilerTests.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/Stage03ContextIdentityPolicyTests.cs",
        "tests/BIMBaoGui.Stage01.Core.Tests/TaskPlanCompilerTests.cs",
        "tests/test_mvd_ifc_normalizer_contract.py",
        "tests/test_official_export_contract_review.py",
        "tests/test_plugin_contract.py",
        "tests/test_stage01_official_hifc_projection.py",
        "tests/test_stage02_component_contract.py",
        "tests/test_v090_release_contract.py",
    )
    assert len(candidates) == 27
    offenders = []
    for relative_path in candidates:
        path = ROOT / relative_path
        assert path.is_file(), f"missing release candidate text: {relative_path}"
        if b"\r" in path.read_bytes():
            offenders.append(relative_path)
    assert not offenders, "release candidate text is not LF-only: " + ", ".join(offenders)


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


def test_ci_push_paths_and_rulepack_tests_cover_current_delivery_branches():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    push = workflow[workflow.index("  push:") : workflow.index("  pull_request:")]
    for branch in ("main", "feat/**", "fix/**"):
        assert branch in push
    for path in ("README.md", "docs/**"):
        assert path in push
    for stale_branch in (
        "feat/stage01-gha-file-initialization",
        "feat/stage01-stage02-context-pipeline",
        "feat/hifc-mapping-gh-baseline-v1",
        "feat/gh-official-hifc-write-integration-v1",
    ):
        assert stale_branch not in push

    compact = re.sub(r"\s+", " ", workflow)
    assert "PYTEST_DISABLE_PLUGIN_AUTOLOAD" in workflow
    assert (
        "python -m pytest tests/test_hbr_rulepack_compiler.py "
        "tests/test_hbr_runtime_packaging_contract.py -q"
    ) in compact
    assert "python -m pytest tests -q" in compact
    assert "BIMBaoGui.Stage01.Core.Tests.csproj -c Release" in workflow
    assert "BIMBaoGui.Stage01.csproj -c Release" in workflow


def test_ci_paths_trigger_for_rulepack_source_baseline_and_compiler():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    push = workflow[workflow.index("  push:") : workflow.index("  pull_request:")]
    pull_request = workflow[
        workflow.index("  pull_request:") : workflow.index("  workflow_dispatch:")
    ]
    for event_paths in (push, pull_request):
        assert '- ".gitattributes"' in event_paths
        assert '"specs/hbr-rules/**"' in event_paths
        assert '"tools/build_hbr_rulepack.py"' in event_paths


def test_ci_pins_all_clean_runner_python_test_dependencies():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    install = workflow_step(workflow, "Install Python test dependencies")
    expected = (
        "python -m pip install --disable-pip-version-check "
        "pytest==8.3.5 jsonschema==4.23.0"
    )
    install_commands = [
        line.strip()
        for line in install.splitlines()
        if "python -m pip install" in line
    ]
    assert install_commands == [expected]
    assert "python-version: \"3.13\"" in workflow


def test_ci_release_plugin_build_treats_compiler_warnings_as_errors():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize(
    "no_warn",
    (
        "1591;0168",
        "0168;1591",
        "0168",
        "",
    ),
)
def test_ci_release_warning_gate_rejects_project_no_warn_mutations(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    no_warn: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)
    project = read(NUGET_PROJECT)
    assert project.count("<NoWarn>1591</NoWarn>") == 1
    project_path.write_text(
        project.replace(
            "<NoWarn>1591</NoWarn>",
            f"<NoWarn>{no_warn}</NoWarn>",
            1,
        ),
        encoding="utf-8",
    )
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


def test_ci_release_warning_gate_rejects_case_variant_no_warn_property(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project = read(NUGET_PROJECT).replace(
        "</PropertyGroup>",
        "    <nowarn>0168</nowarn>\n  </PropertyGroup>",
        1,
    )
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)
    project_path.write_text(project, encoding="utf-8")
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize(
    "repository_mutation",
    (
        "directory-build-props",
        "directory-build-targets",
        "explicit-import",
    ),
)
def test_ci_release_warning_gate_rejects_imported_warning_suppressions(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    repository_mutation: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project = read(NUGET_PROJECT)
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)

    if repository_mutation == "directory-build-props":
        (tmp_path / "Directory.Build.props").write_text(
            "<Project><PropertyGroup>"
            "<NoWarn>$(NoWarn);0168</NoWarn>"
            "</PropertyGroup></Project>",
            encoding="utf-8",
        )
    elif repository_mutation == "directory-build-targets":
        (tmp_path / "Directory.Build.targets").write_text(
            "<Project><PropertyGroup>"
            "<WarningsNotAsErrors>0168</WarningsNotAsErrors>"
            "</PropertyGroup></Project>",
            encoding="utf-8",
        )
    else:
        imported_props = tmp_path / "build" / "WarningSuppressions.props"
        imported_props.parent.mkdir(parents=True)
        imported_props.write_text(
            "<Project><PropertyGroup>"
            "<NoWarn>$(NoWarn);0168</NoWarn>"
            "</PropertyGroup></Project>",
            encoding="utf-8",
        )
        project = project.replace(
            "</Project>",
            '  <Import Project="..\\..\\build\\WarningSuppressions.props" />\n'
            "</Project>",
            1,
        )

    project_path.write_text(project, encoding="utf-8")
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize(
    ("relative_path", "contents"),
    (
        (
            ".editorconfig",
            "root = true\n[*.cs]\ndotnet_diagnostic.CS0168.severity = none\n",
        ),
        (
            "src/.editorconfig",
            "[*.cs]\ndotnet_diagnostic.CS0219.severity = silent\n",
        ),
        (
            "config/quality.globalconfig",
            "is_global = true\n"
            "dotnet_analyzer_diagnostic.severity = suggestion\n",
        ),
        (
            "config/compiler.ruleset",
            "<RuleSet><Rules AnalyzerId=\"Microsoft.CodeAnalysis.CSharp\">"
            "<Rule Id=\"CS0168\" Action=\"None\" />"
            "</Rules></RuleSet>",
        ),
    ),
)
def test_ci_release_warning_gate_rejects_repository_severity_downgrades(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    relative_path: str,
    contents: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)
    project_path.write_text(read(NUGET_PROJECT), encoding="utf-8")
    config_path = tmp_path / relative_path
    config_path.parent.mkdir(parents=True, exist_ok=True)
    config_path.write_text(contents, encoding="utf-8")
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize(
    "property_name",
    (
        "WarningsNotAsErrors",
        "MSBuildWarningsAsMessages",
        "MSBuildWarningsNotAsErrors",
        "WarningsAsMessages",
    ),
)
def test_ci_release_warning_gate_rejects_project_warning_downgrade_properties(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    property_name: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project = read(NUGET_PROJECT).replace(
        "</PropertyGroup>",
        f"    <{property_name}>0168</{property_name}>\n  </PropertyGroup>",
        1,
    )
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)
    project_path.write_text(project, encoding="utf-8")
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize(
    ("property_name", "property_value"),
    (
        ("WarningLevel", "0"),
        ("WarningLevel", "3"),
        ("WarningLevel", "4"),
        ("CodeAnalysisRuleSet", "config/compiler.ruleset"),
        ("CodeAnalysisTreatWarningsAsErrors", "false"),
        ("AnalysisLevel", "None"),
        ("AnalysisMode", "None"),
        ("AnalysisLevelStyle", "None"),
        ("AnalysisModeStyle", "None"),
        ("AnalysisLevelSecurity", "None"),
        ("EnableNETAnalyzers", "false"),
        ("RunAnalyzers", "false"),
        ("RunAnalyzersDuringBuild", "false"),
    ),
)
def test_ci_release_warning_gate_rejects_project_analysis_downgrades(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    property_name: str,
    property_value: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project = read(NUGET_PROJECT).replace(
        "</PropertyGroup>",
        f"    <{property_name}>{property_value}</{property_name}>\n"
        "  </PropertyGroup>",
        1,
    )
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)
    project_path.write_text(project, encoding="utf-8")
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize(
    "item_name",
    ("EditorConfigFiles", "GlobalAnalyzerConfigFiles"),
)
def test_ci_release_warning_gate_rejects_analyzer_config_item_injection(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    item_name: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    project = read(NUGET_PROJECT).replace(
        "</Project>",
        f"  <ItemGroup><{item_name} Include=\"config.txt\" />"
        "</ItemGroup>\n</Project>",
        1,
    )
    project_path = tmp_path / NUGET_PROJECT
    project_path.parent.mkdir(parents=True)
    project_path.write_text(project, encoding="utf-8")
    (tmp_path / "config.txt").write_text(
        "is_global = true\n"
        "dotnet_analyzer_diagnostic.severity = none\n",
        encoding="utf-8",
    )
    monkeypatch.setitem(read.__globals__, "ROOT", tmp_path)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(workflow)


@pytest.mark.parametrize("event", ("push", "pull_request"))
@pytest.mark.parametrize(
    "required_pattern",
    (
        "Directory.Build.*",
        "**/*.props",
        "**/*.targets",
        "**/.editorconfig",
        "**/*.globalconfig",
        "**/*.ruleset",
    ),
)
def test_ci_release_warning_gate_rejects_missing_warning_input_path_patterns(
    event: str,
    required_pattern: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    required_patterns = (
        "Directory.Build.*",
        "**/*.props",
        "**/*.targets",
        "**/.editorconfig",
        "**/*.globalconfig",
        "**/*.ruleset",
    )
    guarded_workflow = workflow
    path_anchor = '      - ".gitattributes"\n'
    assert guarded_workflow.count(path_anchor) == 2
    for pattern in required_patterns:
        path_entry = f'      - "{pattern}"\n'
        if guarded_workflow.count(path_entry) == 0:
            guarded_workflow = guarded_workflow.replace(
                path_anchor,
                f"{path_anchor}{path_entry}",
            )
        assert guarded_workflow.count(path_entry) == 2

    event_start = guarded_workflow.index(f"  {event}:")
    next_event = "  pull_request:" if event == "push" else "  workflow_dispatch:"
    event_end = guarded_workflow.index(next_event, event_start)
    event_block = guarded_workflow[event_start:event_end]
    path_entry = f'      - "{required_pattern}"\n'
    assert event_block.count(path_entry) == 1
    mutant = (
        guarded_workflow[:event_start]
        + event_block.replace(path_entry, "", 1)
        + guarded_workflow[event_end:]
    )

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(mutant)


@pytest.mark.parametrize(
    "downgrade_argument",
    (
        "-p:WarningsNotAsErrors=0168",
        "-p:MSBuildWarningsAsMessages=MSB3270",
        "-p:MSBuildWarningsNotAsErrors=MSB3270",
        "-p:WarningsAsMessages=0168",
        "-p:WarningLevel=3",
        "-p:CodeAnalysisRuleSet=config/compiler.ruleset",
        "-p:CodeAnalysisTreatWarningsAsErrors=false",
        "-p:AnalysisLevel=None",
        "-p:EnableNETAnalyzers=false",
        "-p:RunAnalyzersDuringBuild=false",
        "@config/build.rsp",
    ),
)
def test_ci_release_warning_gate_rejects_command_line_analysis_downgrades(
    downgrade_argument: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    strict_flag = "-p:TreatWarningsAsErrors=true"
    assert workflow.count(strict_flag) == 1
    mutant = workflow.replace(
        strict_flag,
        f"{strict_flag} {downgrade_argument}",
        1,
    )

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(mutant)


@pytest.mark.parametrize(
    "job_flow_control",
    (
        "continue-on-error: true",
        'continue-on-error: "true"',
        "if: false",
        'if: "false"',
    ),
)
def test_ci_release_warning_gate_rejects_job_level_flow_control(
    job_flow_control: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    job_marker = "  build:\n"
    assert workflow.count(job_marker) == 1
    mutant = workflow.replace(
        job_marker,
        f"{job_marker}    {job_flow_control}\n",
        1,
    )

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(mutant)


def test_ci_release_warning_gate_rejects_comments_and_false_overrides():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    flag = "-p:TreatWarningsAsErrors=true"
    project = "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    build_step = workflow_step(workflow, "Build Grasshopper plugin")
    assert workflow.count(flag) == 1
    mutants = (
        workflow.replace(flag, f"# {flag}", 1),
        workflow.replace(flag, f"{flag} -p:TreatWarningsAsErrors=false", 1),
        workflow.replace(flag, f"{flag}Fake", 1),
        workflow.replace(
            build_step,
            build_step.replace(project, f"{project}Fake", 1),
            1,
        ),
        workflow.replace(flag, f"; Write-Host {flag}", 1),
        workflow.replace(build_step, f"{build_step}\n{build_step}", 1),
    )
    for mutant in mutants:
        with pytest.raises(AssertionError):
            assert_release_plugin_build_treats_warnings_as_errors(mutant)


def test_ci_release_warning_gate_rejects_powershell_invocation_bypasses():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    build_step = workflow_step(workflow, "Build Grasshopper plugin")
    project = "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    strict_command = next(
        line.strip().removeprefix("run: ")
        for line in build_step.splitlines()
        if line.strip().startswith("run: dotnet build ")
    )

    def insert_after_build(step: str) -> str:
        replacement = f"{build_step.rstrip()}\n\n{step.rstrip()}\n"
        return workflow.replace(build_step, replacement, 1)

    continued_build_step = build_step.replace(
        f"        run: {strict_command}",
        "        run: |\n"
        f"          {strict_command} `\n"
        "            -p:NoWarn=1591",
        1,
    )
    invocation_rebuild = "\n".join(
        (
            "      - name: Rebuild plugin through PowerShell invocation operator",
            "        shell: pwsh",
            "        run: |",
            f"          & dotnet build {project} -c Release "
            "-p:TreatWarningsAsErrors=false -p:NoWarn=1591",
        )
    )
    dotnet_exe_rebuild = "\n".join(
        (
            "      - name: Rebuild plugin through dotnet executable name",
            "        shell: pwsh",
            f"        run: dotnet.exe build {project} -c Release "
            "-p:TreatWarningsAsErrors=false -p:NoWarn=1591",
        )
    )
    mutants = (
        workflow.replace(build_step, continued_build_step, 1),
        insert_after_build(invocation_rebuild),
        insert_after_build(dotnet_exe_rebuild),
    )
    for mutant in mutants:
        with pytest.raises(AssertionError):
            assert_release_plugin_build_treats_warnings_as_errors(mutant)


@pytest.mark.parametrize(
    "bypass",
    (
        "folded-no-warn",
        "folded-false-warning-gate",
        "powershell-stop-parsing",
    ),
)
def test_ci_release_warning_gate_rejects_folded_and_stop_parsing_bypasses(
    bypass: str,
):
    workflow = read(".github/workflows/build-stage01-gha.yml")
    build_step = workflow_step(workflow, "Build Grasshopper plugin")
    project = "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    strict_command = next(
        line.strip().removeprefix("run: ")
        for line in build_step.splitlines()
        if line.strip().startswith("run: dotnet build ")
    )

    if bypass == "folded-no-warn":
        mutant_step = build_step.replace(
            f"        run: {strict_command}",
            "        run: >\n"
            f"          {strict_command}\n"
            "          -p:NoWarn=1591",
            1,
        )
        mutant = workflow.replace(build_step, mutant_step, 1)
    elif bypass == "folded-false-warning-gate":
        mutant_step = build_step.replace(
            f"        run: {strict_command}",
            "        run: >-\n"
            f"          {strict_command}\n"
            "          -p:TreatWarningsAsErrors=false",
            1,
        )
        mutant = workflow.replace(build_step, mutant_step, 1)
    else:
        stop_parsing_rebuild = "\n".join(
            (
                "      - name: Rebuild plugin through PowerShell stop parsing",
                "        shell: pwsh",
                f"        run: dotnet --% build {project} -c Release "
                "-p:TreatWarningsAsErrors=false -p:NoWarn=1591",
            )
        )
        replacement = (
            f"{build_step.rstrip()}\n\n{stop_parsing_rebuild.rstrip()}\n"
        )
        mutant = workflow.replace(build_step, replacement, 1)

    with pytest.raises(AssertionError):
        assert_release_plugin_build_treats_warnings_as_errors(mutant)


def test_ci_release_warning_gate_rejects_job_flow_and_late_build_bypasses():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    build_step = workflow_step(workflow, "Build Grasshopper plugin")
    verify_step = workflow_step(workflow, "Verify compiled GHA")
    prepare_step = workflow_step(workflow, "Prepare validation artifact")
    upload_step = workflow_step(workflow, "Upload GHA and manifest")
    project = "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
    strict_flag = "-p:TreatWarningsAsErrors=true"
    strict_command = next(
        line.strip().removeprefix("run: ")
        for line in build_step.splitlines()
        if line.strip().startswith("run: dotnet build ")
    )

    def insert_after_build(step: str) -> str:
        replacement = f"{build_step.rstrip()}\n\n{step.rstrip()}\n"
        return workflow.replace(build_step, replacement, 1)

    weak_rebuild = "\n".join(
        (
            "      - name: Rebuild plugin without warning gate",
            "        shell: pwsh",
            f"        run: dotnet build {project} -c Release --no-restore "
            "-p:TreatWarningsAsErrors=false -p:NoWarn=",
        )
    )
    renamed_duplicate = "\n".join(
        (
            "      - name: Rebuild production plugin",
            "        shell: pwsh",
            f"        run: {strict_command}",
        )
    )
    no_warn_rebuild = "\n".join(
        (
            "      - name: Rebuild plugin with suppressed warnings",
            "        shell: pwsh",
            f"        run: dotnet build {project} -c Release --no-restore "
            "-p:TreatWarningsAsErrors=true -p:NoWarn=1591",
        )
    )
    mutants = (
        workflow.replace(
            build_step,
            build_step.replace(
                "        shell: pwsh\n",
                "        continue-on-error: true\n        shell: pwsh\n",
                1,
            ),
            1,
        ),
        workflow.replace(
            build_step,
            build_step.replace(
                "        shell: pwsh\n",
                "        if: false\n        shell: pwsh\n",
                1,
            ),
            1,
        ),
        insert_after_build(weak_rebuild),
        insert_after_build(renamed_duplicate),
        insert_after_build(no_warn_rebuild),
        workflow.replace(
            build_step,
            build_step.replace(
                strict_flag,
                f"{strict_flag} -p:NoWarn=1591",
                1,
            ),
            1,
        ),
        workflow.replace(
            verify_step,
            verify_step.replace(
                "        id: verify\n",
                "        if: always()\n        id: verify\n",
                1,
            ),
            1,
        ),
        workflow.replace(
            verify_step,
            verify_step.replace(
                "        id: verify\n",
                "        continue-on-error: true\n        id: verify\n",
                1,
            ),
            1,
        ),
        workflow.replace(
            prepare_step,
            prepare_step.replace(
                "        shell: pwsh\n",
                "        if: always()\n        shell: pwsh\n",
                1,
            ),
            1,
        ),
        workflow.replace(
            upload_step,
            upload_step.replace(
                "      - name: Upload GHA and manifest\n",
                "      - name: Upload GHA and manifest\n        if: always()\n",
                1,
            ),
            1,
        ),
    )
    for mutant in mutants:
        with pytest.raises(AssertionError):
            assert_release_plugin_build_treats_warnings_as_errors(mutant)


def test_ci_restores_dotnet_before_python_tests_that_use_no_restore():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    restore_marker = "      - name: Restore .NET projects"
    rulepack_marker = (
        "      - name: Run rule pack compiler and packaging contracts"
    )
    repository_marker = "      - name: Run repository contract tests"

    assert workflow.count(restore_marker) == 1
    restore_index = workflow.index(restore_marker)
    assert restore_index < workflow.index(rulepack_marker)
    assert restore_index < workflow.index(repository_marker)
    restore = workflow_step(workflow, "Restore .NET projects")
    assert "BIMBaoGui.Stage01.Core.Tests.csproj" in restore
    assert "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj" in restore
    assert "--no-restore" in workflow_step(workflow, "Run core unit tests")
    assert "--no-restore" in workflow_step(workflow, "Build Grasshopper plugin")


def test_ci_checks_the_committed_event_range_for_whitespace():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    checkout = workflow_step(workflow, "Check out repository")
    gate = workflow_step(workflow, "Check committed diff whitespace")

    assert "fetch-depth: 0" in checkout
    for event_value in (
        "github.event_name",
        "github.event.before",
        "github.event.pull_request.base.sha",
        "github.sha",
    ):
        assert event_value in gate
    assert '$range = "$env:PR_BASE_SHA...$env:HEAD_SHA"' in gate
    assert '$range = "$env:BEFORE_SHA..$env:HEAD_SHA"' in gate
    assert re.search(
        r"git diff --check \$range\s*\n\s*"
        r"if \(\$LASTEXITCODE -ne 0\) \{\s*\n\s*throw ",
        gate,
    )


def test_ci_new_branch_push_uses_default_branch_merge_base():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    gate = workflow_step(workflow, "Check committed diff whitespace")

    assert (
        "DEFAULT_BRANCH: ${{ github.event.repository.default_branch }}"
        in gate
    )
    for required in (
        '} elseif ($env:EVENT_NAME -eq "push") {',
        "$env:BEFORE_SHA -notmatch '^0+$'",
        '$range = "$env:BEFORE_SHA..$env:HEAD_SHA"',
        '$defaultRef = "refs/remotes/origin/$env:DEFAULT_BRANCH"',
        "git rev-parse --verify $defaultRef",
        "$mergeBase = git merge-base $defaultRef $env:HEAD_SHA",
        '$range = "$mergeBase..$env:HEAD_SHA"',
    ):
        assert required in gate
    assert re.search(
        r"git rev-parse --verify \$defaultRef\s*\n\s*"
        r"if \(\$LASTEXITCODE -ne 0\) \{\s*\n\s*throw ",
        gate,
    )
    assert re.search(
        r"\$mergeBase = git merge-base \$defaultRef \$env:HEAD_SHA\s*\n\s*"
        r"if \(\$LASTEXITCODE -ne 0 -or "
        r"\[string\]::IsNullOrWhiteSpace\(\$mergeBase\)\) \{\s*\n\s*throw ",
        gate,
    )
    assert '$parentSha = git rev-parse "$env:HEAD_SHA^"' not in gate


def test_ci_workflow_dispatch_checks_default_branch_merge_base_to_head():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    gate = workflow_step(workflow, "Check committed diff whitespace")
    dispatch_marker = '} elseif ($env:EVENT_NAME -eq "workflow_dispatch") {'

    assert dispatch_marker in gate
    dispatch = gate[gate.index(dispatch_marker) :]
    for required in (
        "Repository default branch is unavailable.",
        '$defaultRef = "refs/remotes/origin/$env:DEFAULT_BRANCH"',
        "git rev-parse --verify $defaultRef",
        "$mergeBase = git merge-base $defaultRef $env:HEAD_SHA",
        '$range = "$mergeBase..$env:HEAD_SHA"',
    ):
        assert required in dispatch
    assert 'git rev-parse "$env:HEAD_SHA^"' not in gate


def test_ci_unknown_event_fails_closed_instead_of_using_parent_only_diff():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    gate = workflow_step(workflow, "Check committed diff whitespace")

    assert 'throw "Unsupported GitHub event: $env:EVENT_NAME"' in gate
    assert "$parentSha" not in gate


def test_ci_fails_on_scan_errors_or_any_nuget_vulnerability():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    gate = workflow_step(workflow, "Fail on vulnerable NuGet packages")
    command = (
        "dotnet list src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj "
        "package --vulnerable --include-transitive --format json"
    )

    assert command in gate
    assert re.search(
        re.escape(command)
        + r"\s*\n\s*if \(\$LASTEXITCODE -ne 0\) \{\s*\n\s*throw ",
        gate,
    )
    assert "ConvertFrom-Json -ErrorAction Stop" in gate
    assert "$package.vulnerabilities" in gate
    assert re.search(
        r"if \(\$vulnerablePackages\.Count -gt 0\) \{[\s\S]*?throw ",
        gate,
    )


def test_ci_nuget_scan_fails_closed_when_projects_is_empty():
    workflow = read(".github/workflows/build-stage01-gha.yml")
    gate = workflow_step(workflow, "Fail on vulnerable NuGet packages")

    assert (
        "$projects = @($report.projects | Where-Object { $null -ne $_ })"
        in gate
    )
    assert re.search(
        r"if \(\$projects\.Count -eq 0\) \{\s*\n\s*throw ",
        gate,
    )
    assert "foreach ($project in $projects)" in gate
    assert "foreach ($project in @($report.projects))" not in gate

    _, vulnerability = healthy_nuget_reports()
    result = run_nuget_gate({"version": 1, "projects": []}, vulnerability)
    assert result.returncode != 0
    assert "exactly one project" in (result.stdout + result.stderr)


@pytest.mark.parametrize("frameworks", [None, []], ids=["missing", "empty"])
def test_ci_nuget_scan_rejects_parseable_project_without_frameworks(frameworks):
    inventory, vulnerability = healthy_nuget_reports()
    project = {"path": inventory["projects"][0]["path"]}
    if frameworks is not None:
        project["frameworks"] = frameworks
    mutant = {"version": 1, "projects": [project]}

    result = run_nuget_gate(mutant, vulnerability)

    assert result.returncode != 0
    assert "does not contain a framework" in (result.stdout + result.stderr)


def test_ci_nuget_scan_rejects_unexpected_project_identity():
    inventory, vulnerability = healthy_nuget_reports()
    inventory["projects"][0]["path"] = str(ROOT / "unexpected.csproj")

    result = run_nuget_gate(inventory, vulnerability)

    assert result.returncode != 0
    assert "unexpected project" in (result.stdout + result.stderr).lower()


def test_ci_nuget_scan_accepts_framework_with_zero_packages():
    inventory, vulnerability = healthy_nuget_reports()

    result = run_nuget_gate(inventory, vulnerability)

    assert result.returncode == 0, result.stdout + result.stderr


def test_stage03_docs_teach_exact_grasshopper_wiring_and_rerun_semantics():
    for path in (
        "README.md",
        "docs/revit2020-v090-acceptance-checklist.md",
    ):
        text = read(path)
        for required in (
            "将 Grasshopper `Boolean Toggle` 接到“全部通过才导出”",
            "`true`（默认值）= Strict",
            "所有活动业务阻断处理完才导出",
            "`false` = Force 测试放行",
            "将非空 `Panel` 文本接到“强制原因”",
            "技术致命错误始终阻断",
            "“执行”建议接 `Button`",
            "切换模式、强制原因、输出目录或其他输入后",
            "`false → true` 上升沿重新运行",
            "卡片显示 Strict / Force、字段计数、运行状态",
            "RAW IFC、HIFC-MVD IFC 和 fields JSON 三条路径",
        ):
            assert required in text, f"{path} missing Stage03 wiring: {required}"


def test_task12_checklist_has_blank_evidence_slots_for_each_stage03_scenario():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    scenarios = (
        "STRICT_BLOCKED",
        "STRICT_CLEAN_EXPORT",
        "FORCE_EMPTY_REASON",
        "FORCE_BUSINESS_BYPASS",
        "FORCE_TECHNICAL_FATAL",
    )
    statuses = (
        "PASS",
        "NOT_APPLICABLE",
        "MISSING_CARRIER",
        "CARRIER_CATEGORY_MISMATCH",
        "CARRIER_NAME_MISMATCH",
        "AMBIGUOUS_CARRIER",
        "MISSING_PARAMETER",
        "EMPTY_REQUIRED_VALUE",
        "INVALID_VALUE",
        "RULE_NOT_IMPLEMENTED",
        "UNCLASSIFIED_REQUIREMENT",
        "IFC_OWNER_NOT_FOUND",
        "IFC_VALUE_MISMATCH",
        "NOT_EVALUATED",
    )
    evidence_fields = (
        "runId",
        "mode",
        "去除首尾空白后的记录原因",
        "allowExport",
        "RAW IFC 路径",
        "RAW IFC SHA-256",
        "HIFC-MVD IFC 路径",
        "HIFC-MVD IFC SHA-256",
        "fields JSON 路径",
        "fields JSON SHA-256",
        "Stage02 failure report 路径",
        "Stage02 failure report SHA-256",
        "Stage03 failure report 路径",
        "Stage03 failure report SHA-256",
        "预期结果",
        "实际结果",
    )

    assert "不适用项必须在“实际记录”栏明确填写 `N/A`" in checklist
    for native_identity in (
        "reportId",
        "inputSignature",
        "fileGuid",
        "documentFingerprint",
        "packageId / version / hash",
        "occurredUtc / occurredLocal",
        "场景时间窗",
    ):
        assert native_identity in checklist
    assert "Stage03 failure report 证据槽只接受本场景同一 `runId`" in checklist
    assert "Stage02 failure report 不使用 Stage03 `runId` 归属" in checklist
    common_start = checklist.index("## 通用证据记录")
    scenario_start = checklist.index("## Stage03 分场景证据记录")
    common = checklist[common_start:scenario_start]
    assert "Stage02 failure report" not in common
    for index, scenario in enumerate(scenarios):
        marker = f"### `{scenario}`"
        assert marker in checklist
        start = checklist.index(marker)
        end = (
            checklist.index(f"### `{scenarios[index + 1]}`", start)
            if index + 1 < len(scenarios)
            else checklist.index("## IFC owner 策略", start)
        )
        section = checklist[start:end]
        assert "| 证据项 | 预期 | 实际记录 |" in section
        for field in evidence_fields:
            assert re.search(
                rf"^\| {re.escape(field)} \| [^|\n]* \|  \|$",
                section,
                re.MULTILINE,
            ), f"{scenario} missing blank slot: {field}"
        assert_task12_failure_report_rows(section, scenario)
        assert "| Stage03FieldStatus | 实际数量 |" in section
        for status in statuses:
            assert f"| `{status}` |  |" in section
        assert "`N/A`" in section

    for obsolete_table in (
        "| Stage03FieldStatus | Strict 数量 | Force 数量 |",
        "### Stage03 产物路径与哈希",
        "### Strict / Force 门禁差异",
    ):
        assert obsolete_table not in checklist


def test_task12_strict_clean_is_conditional_until_authoritative_classification():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    mandatory_marker = "当前 v0.9.0 强制执行且分别留证的四个场景"
    conditional_marker = "`STRICT_CLEAN_EXPORT` 是条件场景"

    assert mandatory_marker in checklist
    assert conditional_marker in checklist
    mandatory_start = checklist.index(mandatory_marker)
    mandatory_end = checklist.index(conditional_marker, mandatory_start)
    mandatory = checklist[mandatory_start:mandatory_end]
    for scenario in (
        "STRICT_BLOCKED",
        "FORCE_EMPTY_REASON",
        "FORCE_BUSINESS_BYPASS",
        "FORCE_TECHNICAL_FATAL",
    ):
        assert f"`{scenario}`" in mandatory
    assert "`STRICT_CLEAN_EXPORT`" not in mandatory

    for required in (
        "359/359",
        "均为 `UNCLASSIFIED`",
        "不得伪造权威分类",
        "仅在权威分类完成后适用",
        "不阻塞当前 v0.9.0 诚实验收",
    ):
        assert required in checklist
    assert "五个场景必须分别运行并分别留证" not in checklist

    clean_start = checklist.index("### `STRICT_CLEAN_EXPORT`")
    clean_end = checklist.index("### `FORCE_EMPTY_REASON`", clean_start)
    clean_section = checklist[clean_start:clean_end]
    assert "条件场景" in clean_section
    assert "仅在权威分类完成后适用" in clean_section


def test_task12_stage02_locks_guid_sentinels_reopen_and_rvt_switch_evidence():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    bindings = json.loads(
        read("specs/hifc-mapping/v1/generated/GH_HIFC_ParameterBindings.json")
    )["bindings"]
    by_guid = {item["parameterGuid"]: item for item in bindings}
    project_guid = "4225a5de-c942-54aa-874a-28a1e67ce39c"
    instance_guid = "7dc1a82e-f3d0-5210-b3bf-6b517da25d80"
    assert by_guid[project_guid]["category"] == "OST_ProjectInformation"
    assert by_guid[instance_guid]["category"] == "OST_Levels"
    assert by_guid[instance_guid]["bindingScope"] == "INSTANCE"

    start = checklist.index("## Stage02 构件与属性准备")
    end = checklist.index("## Stage03 Strict", start)
    section = checklist[start:end]
    for required in (
        "项目信息属性面板 GUID 哨兵",
        "实例/类型属性面板 GUID 哨兵",
        project_guid,
        instance_guid,
        "HBR-S2-PROJECT-SENTINEL-v090",
        "HBR-S2-INSTANCE-SENTINEL-v090",
        "保存 → 关闭 → 重新打开",
        "重开后 GUID 回读显示值",
        "切换 RVT 后旧预览失效",
        "原 RVT DocumentFingerprint / previewHash",
        "切换后 RVT DocumentFingerprint",
        "旧预览确认写入尝试",
        "明确显示 `结果过期`",
        "不得进入 Revit 写入队列",
    ):
        assert required in section

    for evidence_row in (
        "保存前属性面板截图路径 / SHA-256",
        "重开后属性面板截图路径 / SHA-256",
        "保存、关闭、重新打开时间",
        "原 RVT 路径",
        "切换后 RVT 路径",
        "切换后 GH 状态截图路径 / SHA-256",
        "旧预览确认写入尝试结果",
    ):
        assert re.search(
            rf"^\| {re.escape(evidence_row)} \| [^|\n]* \|  \|$",
            section,
            re.MULTILINE,
        ), f"Stage02 missing blank evidence slot: {evidence_row}"


def test_task12_stage03_locks_coordinate_values_and_source_integrity_evidence():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    rules = json.loads(
        read("specs/hbr-rules/v1/source/hbr_rule_source.v1.json")
    )["properties"]
    by_id = {item["propertyId"]: item for item in rules}
    coordinates = (
        (
            "基点坐标 X",
            "6b407894-09d4-529a-9f9f-a031219cdeaa",
        ),
        (
            "基点坐标 Y",
            "1a64ef8d-e97c-5fa1-b53f-52b969b6198a",
        ),
        (
            "基点高程",
            "50164757-c346-5005-a1b8-7b423c6b8de5",
        ),
    )
    for property_name, parameter_guid in coordinates:
        rule = by_id[parameter_guid]
        assert rule["revit"]["parameterGuid"] == parameter_guid
        assert rule["ifc"]["entity"] == "IfcProject"
        assert rule["ifc"]["propertySet"] == "Pset_申报信息属性集"
        assert rule["ifc"]["property"] == property_name
        assert rule["ifc"]["declaredType"] == "IfcReal"

    scenario_bounds = (
        ("STRICT_CLEAN_EXPORT", "FORCE_EMPTY_REASON"),
        ("FORCE_BUSINESS_BYPASS", "FORCE_TECHNICAL_FATAL"),
    )
    field_header = (
        "| 字段 | Revit 参数 GUID | Revit 显示值 | Revit 显示单位 | "
        "final IFC 实体 | final IFC Pset | final IFC 属性 | final IFC 类型 | "
        "final IFC 值 | 对照结论 |"
    )
    for scenario, next_scenario in scenario_bounds:
        start = checklist.index(f"### `{scenario}`")
        end = checklist.index(f"### `{next_scenario}`", start)
        section = checklist[start:end]
        assert field_header in section
        for property_name, parameter_guid in coordinates:
            assert (
                f"| {property_name} | `{parameter_guid}` |  |  | "
                f"`IfcProject` | `Pset_申报信息属性集` | `{property_name}` | "
                "`IfcReal` |  |  |"
            ) in section

        for evidence_row in (
            "源 RVT 路径",
            "Stage03 执行开始前源 RVT SHA-256",
            "Stage03 执行结束后源 RVT SHA-256",
            "RAW IFC 转译开始前 SHA-256",
            "RAW IFC 转译结束后 SHA-256",
        ):
            assert re.search(
                rf"^\| {re.escape(evidence_row)} \| [^|\n]* \|  \|$",
                section,
                re.MULTILINE,
            ), f"{scenario} missing blank integrity slot: {evidence_row}"


def test_task12_validator_rejects_fatal_stage03_allow_na_mutation():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    fatal_path = (
        "| Stage03 failure report 路径 | 必须记录本场景 `runId` 的技术致命失败报告；"
        "禁止填 `N/A` |  |"
    )
    fatal_sha = (
        "| Stage03 failure report SHA-256 | 必须记录本场景 `runId` 报告哈希；"
        "禁止填 `N/A` |  |"
    )
    bad_path = fatal_path.replace("禁止填 `N/A`", "允许填 `N/A`")
    bad_sha = fatal_sha.replace("禁止填 `N/A`", "允许填 `N/A`")
    assert checklist.count(fatal_path) == 1
    assert checklist.count(fatal_sha) == 1
    assert "允许填 `N/A`" not in checklist

    mutant = checklist.replace(fatal_path, bad_path, 1).replace(
        fatal_sha,
        bad_sha,
        1,
    )
    assert mutant.replace("允许填 `N/A`", "禁止填 `N/A`") == checklist
    start = mutant.index("### `FORCE_TECHNICAL_FATAL`")
    end = mutant.index("## IFC owner 策略", start)

    try:
        assert_task12_failure_report_rows(
            mutant[start:end],
            "FORCE_TECHNICAL_FATAL",
        )
    except AssertionError:
        return
    raise AssertionError(
        "Task12 validator accepted fatal Stage03 rules that allow N/A"
    )


def test_force_empty_reason_requires_independent_fields_path_and_sha():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    start = checklist.index("### `FORCE_EMPTY_REASON`")
    end = checklist.index("### `FORCE_BUSINESS_BYPASS`", start)
    section = checklist[start:end]

    assert_force_empty_reason_fields_evidence(section)

    required_path = (
        "| fields JSON 路径 | 本场景独立路径；必须回填 |  |"
    )
    required_sha = (
        "| fields JSON SHA-256 | 本场景文件哈希；必须回填 |  |"
    )
    old_path = "| fields JSON 路径 | 按实际回填，不适用填 `N/A` |  |"
    old_sha = "| fields JSON SHA-256 | 按实际回填，不适用填 `N/A` |  |"
    assert section.count(required_path) == 1
    assert section.count(required_sha) == 1

    for mutant in (
        section.replace(required_path, old_path, 1),
        section.replace(required_sha, old_sha, 1),
    ):
        with pytest.raises(AssertionError):
            assert_force_empty_reason_fields_evidence(mutant)


def test_task12_validator_rejects_stage02_stage03_runid_mutations():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    start = checklist.index("### `STRICT_BLOCKED`")
    end = checklist.index("### `STRICT_CLEAN_EXPORT`", start)
    section = checklist[start:end]

    stage02_mutant = section.replace("reportId", "runId", 1)
    assert stage02_mutant != section
    with pytest.raises(AssertionError):
        assert_task12_failure_report_rows(stage02_mutant, "STRICT_BLOCKED")

    for suffix in ("路径", "SHA-256"):
        stage03_row = re.search(
            rf"^\| Stage03 failure report {suffix} \| ([^|\n]*) \|  \|$",
            section,
            re.MULTILINE,
        )
        assert stage03_row
        mutated_expected = stage03_row.group(1).replace("`runId`", "报告", 1)
        assert mutated_expected != stage03_row.group(1)
        stage03_mutant = (
            section[: stage03_row.start(1)]
            + mutated_expected
            + section[stage03_row.end(1) :]
        )
        with pytest.raises(AssertionError):
            assert_task12_failure_report_rows(stage03_mutant, "STRICT_BLOCKED")


def test_task12_validator_rejects_missing_stage02_native_identity():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    start = checklist.index("### `STRICT_BLOCKED`")
    end = checklist.index("### `STRICT_CLEAN_EXPORT`", start)
    section = checklist[start:end]
    for native_identity in (
        "reportId",
        "inputSignature",
        "fileGuid",
        "documentFingerprint",
        "packageId / version / hash",
        "occurredUtc / occurredLocal",
        "场景时间窗",
    ):
        mutant = section.replace(native_identity, "缺失身份", 1)
        assert mutant != section
        with pytest.raises(AssertionError):
            assert_task12_failure_report_rows(mutant, "STRICT_BLOCKED")


def test_force_reason_docs_match_trimmed_record_semantics():
    readme = read("README.md")
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    gate = read("src/BIMBaoGui.Stage01/Stage03/Stage03ExportGatePolicy.cs")

    assert "return (value ?? string.Empty).Trim();" in gate
    for text in (readme, checklist):
        assert "去除首尾空白后的记录原因" in text
        assert "原始强制原因" not in text


def test_revit2020_v090_acceptance_checklist_covers_runtime_chain():
    checklist = read("docs/revit2020-v090-acceptance-checklist.md")
    assert "- [x]" not in checklist.casefold()
    assert r"D:\18_建模项目\2026.07_湖北银行报规\3D\20260731test02.rvt" in checklist
    for component in (
        "湖北BIM报规｜01 文件初始化",
        "湖北BIM报规｜02 构件与属性准备",
        "湖北BIM报规｜03 检测、导出与 H-IFC 转译",
    ):
        assert component in checklist
    for required in (
        "坐标 X / Y",
        "高程",
        "真北",
        "Revit UI 可见、可编辑",
        "保存",
        "重新打开",
        "Stage03 Strict",
        "Stage03 Force",
        "-RAW.ifc",
        "-HIFC-MVD.ifc",
        "-fields.json",
        "RAW IFC SHA-256",
        "活动 GHA 同目录",
        "BIMBaoGui.Stage01.gha",
    ):
        assert required in checklist
    assert "SHA-256" in checklist
    assert "Stage 04" not in checklist
    assert "04 MVD IFC 规范化" not in checklist
    assert "03 官方 H-IFC 属性写入" not in checklist
    assert "API_SUCCESS" not in checklist
