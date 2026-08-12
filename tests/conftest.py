"""Narrow pytest policies shared by repository contract tests.

The Windows Server 2025 GitHub-hosted image can occasionally spend more than
30 seconds starting Windows PowerShell 5.1 and reflection-loading the freshly
built net48 GHA.  The affected assertion is still valuable, so do not skip or
xfail it.  Extend only that exact manifest-resource probe while leaving every
other subprocess timeout unchanged.
"""

from __future__ import annotations

import subprocess
from collections.abc import Sequence
from typing import Any

import pytest


_MANIFEST_PROBE_TEST = (
    "test_stage01_real_build_is_incremental_and_embeds_only_the_generated_pack"
)
_MANIFEST_PROBE_MARKER = "GetManifestResourceNames"
_MANIFEST_PROBE_TIMEOUT_SECONDS = 120


def _is_manifest_resource_probe(command: object) -> bool:
    if isinstance(command, (str, bytes)):
        return _MANIFEST_PROBE_MARKER in str(command)
    if not isinstance(command, Sequence):
        return False
    return any(_MANIFEST_PROBE_MARKER in str(part) for part in command)


@pytest.fixture(autouse=True)
def _stabilize_windows_manifest_resource_probe(
    monkeypatch: pytest.MonkeyPatch,
    request: pytest.FixtureRequest,
):
    """Preserve the probe and its assertions, but remove a runner cold-start flake."""

    if request.node.name != _MANIFEST_PROBE_TEST:
        yield
        return

    original_run = subprocess.run

    def run_with_manifest_probe_budget(*popenargs: Any, **kwargs: Any):
        command = kwargs.get("args")
        if command is None and popenargs:
            command = popenargs[0]
        if _is_manifest_resource_probe(command):
            configured = kwargs.get("timeout")
            if configured is None or configured < _MANIFEST_PROBE_TIMEOUT_SECONDS:
                kwargs["timeout"] = _MANIFEST_PROBE_TIMEOUT_SECONDS
        return original_run(*popenargs, **kwargs)

    monkeypatch.setattr(subprocess, "run", run_with_manifest_probe_budget)
    yield
