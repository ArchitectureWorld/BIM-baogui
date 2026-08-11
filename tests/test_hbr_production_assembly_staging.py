import hashlib
import os
import subprocess
import time
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PRODUCTION_PROJECT = ROOT / "src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj"
TEST_PROJECT = (
    ROOT
    / "tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj"
)
FIXTURE_NAME = "BIMBaoGui.Stage01.production.dll"
GHA_NAME = "BIMBaoGui.Stage01.gha"
SOURCE_TREE_ROOTS = (ROOT / "src", ROOT / "tests")


def _run_dotnet(*arguments, check=True):
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
    if check:
        assert result.returncode == 0, result.stdout + result.stderr
    return result


def _property_arguments(properties):
    return [f"/p:{name}={value}" for name, value in properties.items()]


def _restore(project, properties):
    return _run_dotnet(
        "restore",
        project,
        *_property_arguments(properties),
        "--verbosity",
        "minimal",
    )


def _build(project, properties):
    return _run_dotnet(
        "build",
        project,
        *_property_arguments(properties),
        "--no-restore",
        "--verbosity",
        "minimal",
    )


def _clean_projects(project_properties, output_root, configuration):
    failures = []
    for project, properties in project_properties:
        result = _run_dotnet(
            "clean",
            project,
            *_property_arguments(properties),
            "--verbosity",
            "minimal",
            check=False,
        )
        if result.returncode != 0:
            failures.append(result.stdout + result.stderr)

    fixtures = sorted(output_root.rglob(FIXTURE_NAME))
    gha_files = sorted(output_root.rglob(GHA_NAME))
    source_tree_residue = _source_tree_configuration_files(configuration)
    assert not fixtures, f"dotnet clean left staged fixtures: {fixtures}"
    assert not gha_files, f"dotnet clean left production/staged GHA files: {gha_files}"
    assert not source_tree_residue, (
        "isolated build left configuration files in the source tree: "
        f"{source_tree_residue}"
    )
    assert not failures, "\n".join(failures)


def _source_tree_configuration_files(configuration):
    return sorted(
        path
        for tree_root in SOURCE_TREE_ROOTS
        for path in tree_root.rglob("*")
        if path.is_file() and configuration in path.parts
    )


def _configuration(prefix):
    return f"{prefix}_{uuid.uuid4().hex}"


def _path_property(path):
    return f"{path}{os.sep}"


def _isolated_properties(configuration, output_property, output_path, obj_path):
    return {
        "Configuration": configuration,
        output_property: _path_property(output_path),
        "BaseIntermediateOutputPath": _path_property(obj_path),
        "DefaultItemExcludesInProjectFolder": "obj/**",
        "RestoreRecursive": "false",
    }


def _test_project_properties(production_properties):
    return {
        **production_properties,
        "BuildProjectReferences": "false",
    }


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
    configuration = _configuration("HbrOutputContractTest")
    output_root = tmp_path / "custom-output"
    production_properties = _isolated_properties(
        configuration,
        "OutputPath",
        output_root,
        tmp_path / "obj" / "BIMBaoGui.Stage01",
    )
    production_properties["AssemblyVersion"] = "9.9.9.9"
    test_properties = _test_project_properties(
        _isolated_properties(
            configuration,
            "OutputPath",
            output_root,
            tmp_path / "obj" / "BIMBaoGui.Stage01.Core.Tests",
        )
    )
    test_properties["AssemblyVersion"] = "9.9.9.9"

    assert not list(output_root.rglob(FIXTURE_NAME))
    assert not _source_tree_configuration_files(configuration)
    try:
        _restore(PRODUCTION_PROJECT, production_properties)
        _restore(TEST_PROJECT, test_properties)
        _build(PRODUCTION_PROJECT, production_properties)
        _build(TEST_PROJECT, test_properties)

        production_assembly = _single_file(
            output_root,
            "BIMBaoGui.Stage01.dll",
        )
        staged_fixture = _single_file(output_root, FIXTURE_NAME)
        production_gha = production_assembly.with_suffix(".gha")
        staged_gha = staged_fixture.with_name(GHA_NAME)
        assert production_gha.is_file()
        assert staged_gha.is_file()
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
        assert _sha256(production_gha) == _sha256(production_assembly)
        assert _sha256(staged_gha) == _sha256(production_assembly)
    finally:
        _clean_projects(
            (
                (TEST_PROJECT, test_properties),
                (PRODUCTION_PROJECT, production_properties),
            ),
            output_root,
            configuration,
        )


def test_base_output_incremental_staging_and_clean(tmp_path):
    configuration = _configuration("HbrBaseOutputContractTest")
    output_root = tmp_path / "base-output"
    production_properties = _isolated_properties(
        configuration,
        "BaseOutputPath",
        output_root,
        tmp_path / "obj" / "BIMBaoGui.Stage01",
    )
    test_properties = _test_project_properties(
        _isolated_properties(
            configuration,
            "BaseOutputPath",
            output_root,
            tmp_path / "obj" / "BIMBaoGui.Stage01.Core.Tests",
        )
    )

    assert not list(output_root.rglob(FIXTURE_NAME))
    assert not _source_tree_configuration_files(configuration)
    try:
        _restore(PRODUCTION_PROJECT, production_properties)
        _restore(TEST_PROJECT, test_properties)
        _build(PRODUCTION_PROJECT, production_properties)
        _build(TEST_PROJECT, test_properties)

        production_assembly = _single_file(
            output_root,
            "BIMBaoGui.Stage01.dll",
        )
        staged_fixture = _single_file(output_root, FIXTURE_NAME)
        production_gha = production_assembly.with_suffix(".gha")
        staged_gha = staged_fixture.with_name(GHA_NAME)
        assert production_gha.is_file()
        assert staged_gha.is_file()
        production_hash = _sha256(production_assembly)
        staged_hash = _sha256(staged_fixture)
        staged_timestamp = staged_fixture.stat().st_mtime_ns

        assert staged_hash == production_hash
        assert _sha256(production_gha) == production_hash
        assert _sha256(staged_gha) == production_hash
        time.sleep(1.1)
        _build(TEST_PROJECT, test_properties)

        staged_fixture = _single_file(output_root, FIXTURE_NAME)
        assert _sha256(staged_fixture) == production_hash
        assert staged_fixture.stat().st_mtime_ns == staged_timestamp
    finally:
        _clean_projects(
            (
                (TEST_PROJECT, test_properties),
                (PRODUCTION_PROJECT, production_properties),
            ),
            output_root,
            configuration,
        )


def test_staging_rejects_stale_same_identity_gha(tmp_path):
    configuration = _configuration("HbrStaleGhaContractTest")
    output_root = tmp_path / "stale-output"
    production_properties = _isolated_properties(
        configuration,
        "OutputPath",
        output_root,
        tmp_path / "obj" / "BIMBaoGui.Stage01",
    )
    test_properties = _test_project_properties(
        _isolated_properties(
            configuration,
            "OutputPath",
            output_root,
            tmp_path / "obj" / "BIMBaoGui.Stage01.Core.Tests",
        )
    )

    try:
        _restore(PRODUCTION_PROJECT, production_properties)
        _restore(TEST_PROJECT, test_properties)
        _build(PRODUCTION_PROJECT, production_properties)
        production_assembly = _single_file(
            output_root,
            "BIMBaoGui.Stage01.dll",
        )
        production_gha = production_assembly.with_suffix(".gha")
        assert _assembly_version(production_gha) == _assembly_version(
            production_assembly
        )
        with production_gha.open("ab") as stream:
            stream.write(b"stale-same-identity")

        result = _run_dotnet(
            "build",
            TEST_PROJECT,
            *_property_arguments(test_properties),
            "--no-restore",
            "--verbosity",
            "minimal",
            check=False,
        )

        assert result.returncode != 0, (
            "test staging accepted a stale GHA with the same assembly identity"
        )
        assert "production DLL/GHA content mismatch" in (
            result.stdout + result.stderr
        )
    finally:
        for project, properties in (
            (TEST_PROJECT, test_properties),
            (PRODUCTION_PROJECT, production_properties),
        ):
            _run_dotnet(
                "clean",
                project,
                *_property_arguments(properties),
                "--verbosity",
                "minimal",
                check=False,
            )
        assert not _source_tree_configuration_files(configuration)


def test_production_gha_is_registered_in_filewrites_and_cleaned(tmp_path):
    configuration = _configuration("HbrGhaCleanContractTest")
    output_root = tmp_path / "production-output"
    filewrites_manifest = tmp_path / "gha-filewrites.txt"
    after_targets = tmp_path / "capture-filewrites.targets"
    after_targets.write_text(
        """<Project>
  <Target Name="CaptureHbrGhaFileWrites" AfterTargets="CopyAsGha">
    <WriteLinesToFile File="{manifest}" Lines="@(FileWrites->'%(FullPath)')" Overwrite="true" />
  </Target>
</Project>
""".format(manifest=filewrites_manifest.as_posix()),
        encoding="utf-8",
    )
    properties = _isolated_properties(
        configuration,
        "OutputPath",
        output_root,
        tmp_path / "obj" / "BIMBaoGui.Stage01",
    )
    properties["CustomAfterMicrosoftCommonTargets"] = after_targets

    try:
        _restore(PRODUCTION_PROJECT, properties)
        _build(PRODUCTION_PROJECT, properties)
        production_assembly = _single_file(
            output_root,
            "BIMBaoGui.Stage01.dll",
        )
        production_gha = production_assembly.with_suffix(".gha")
        assert production_gha.is_file()
        filewrites = {
            Path(line).resolve()
            for line in filewrites_manifest.read_text(
                encoding="utf-8"
            ).splitlines()
            if line.strip()
        }
        assert production_gha.resolve() in filewrites

        _run_dotnet(
            "clean",
            PRODUCTION_PROJECT,
            *_property_arguments(properties),
            "--verbosity",
            "minimal",
        )
        assert not production_gha.exists()
    finally:
        _run_dotnet(
            "clean",
            PRODUCTION_PROJECT,
            *_property_arguments(properties),
            "--verbosity",
            "minimal",
            check=False,
        )
        assert not _source_tree_configuration_files(configuration)
