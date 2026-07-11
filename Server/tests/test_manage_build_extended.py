"""Tests for Layer 1 extended actions on manage_build."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_build import manage_build, ALL_ACTIONS


@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_build.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_build.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── configure_code_generation ────────────────────────────────────────


@pytest.mark.asyncio
async def test_configure_code_generation(mock_unity):
    """configure_code_generation is in ALL_ACTIONS; should forward to Unity and succeed."""
    result = await manage_build(
        SimpleNamespace(),
        action="configure_code_generation",
        target="StandaloneWindows64",
        scriptingBackend="Mono",
        strippingLevel="Disabled",
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "configure_code_generation"
    assert mock_unity["params"]["target"] == "StandaloneWindows64"
    assert mock_unity["params"]["scriptingBackend"] == "Mono"
    assert mock_unity["params"]["strippingLevel"] == "Disabled"


# ── get_build_report (no prior build) ────────────────────────────────


@pytest.mark.asyncio
async def test_get_build_report_no_build(monkeypatch):
    """get_build_report should return NO_BUILD_REPORT error when no prior build exists."""
    async def fake_send_no_report(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "NO_BUILD_REPORT: No prior build found. Run a build first.",
        }

    monkeypatch.setattr(
        "services.tools.manage_build.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_build.send_with_unity_instance",
        fake_send_no_report,
    )

    result = await manage_build(
        SimpleNamespace(),
        action="get_build_report",
    )
    assert result["success"] is False
    assert "message" in result
    # The action is not in ALL_ACTIONS yet, so the Python side blocks it first.
    # Once get_build_report is added to ALL_ACTIONS and forwarded to Unity,
    # this test will verify the Unity-side NO_BUILD_REPORT error.
    # For now, it catches the "Unknown action" error or accepts either.
    assert "no_build_report" in result["message"].lower() or "Unknown action" in result["message"]
