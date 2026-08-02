import hashlib
import os
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PRODUCTION_PROJECT = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
TEST_PROJECT = (
    ROOT
    / "tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj"
)
TEST_PROJECT_DIRECTORY = TEST_PROJECT.parent
FIXTURE_NAME = "BIMBaoGui.Stage01.production.dll"


def _run_dotnet(*arguments):
    environment = os.environ.copy()
    environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    result = subprocess.run(
        ["dotnet", *map(str, arguments)],
        cwd=ROOT,
        env=environment,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    return result


def _single_file(directory, name):
    matches = sorted(directory.rglob(name))
    assert len(matches) == 1, f"expected one {name} below {directory}, got {matches}"
    return matches[0]


def _sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _assembly_version(path):
    escaped = str(path.resolve()).replace("'", "''")
    command = (
        "[Reflection.AssemblyName]::GetAssemblyName('"
        + escaped
        + "').Version.ToString()"
    )
    result = subprocess.run(
        [
            "powershell.exe",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            command,
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    return result.stdout.strip()


def test_staged_fixture_matches_custom_output_target_path(tmp_path):
    configuration = "HbrOutputContractTest"
    _run_dotnet(
        "build",
        PRODUCTION_PROJECT,
        "-c",
        configuration,
        "--no-restore",
        "/p:AssemblyVersion=1.2.3.4",
        "--verbosity",
        "minimal",
    )

    custom_output = tmp_path / "custom-output"
    _run_dotnet(
        "build",
        TEST_PROJECT,
        "-c",
        configuration,
        "--no-restore",
        f"/p:OutputPath={custom_output}{os.sep}",
        "/p:AssemblyVersion=9.9.9.9",
        "--verbosity",
        "minimal",
    )

    production_assembly = _single_file(
        custom_output,
        "BIMBaoGui.Stage01.dll",
    )
    staged_fixture = _single_file(custom_output, FIXTURE_NAME)
    production_identity = _assembly_version(production_assembly)
    staged_identity = _assembly_version(staged_fixture)

    assert production_identity == "9.9.9.9"
    assert (
        _sha256(staged_fixture),
        staged_identity,
    ) == (
        _sha256(production_assembly),
        production_identity,
    ), (
        "staged fixture did not come from this build's production TargetPath: "
        f"fixture={staged_fixture} version={staged_identity}, "
        f"production={production_assembly} version={production_identity}"
    )


def test_clean_removes_staged_production_fixture():
    configuration = "HbrCleanContractTest"
    _run_dotnet(
        "build",
        TEST_PROJECT,
        "-c",
        configuration,
        "--no-restore",
        "--verbosity",
        "minimal",
    )
    fixture = _single_file(
        TEST_PROJECT_DIRECTORY / "bin" / configuration,
        FIXTURE_NAME,
    )

    _run_dotnet(
        "clean",
        TEST_PROJECT,
        "-c",
        configuration,
        "--verbosity",
        "minimal",
    )

    assert not fixture.exists(), f"dotnet clean left stale fixture: {fixture}"
