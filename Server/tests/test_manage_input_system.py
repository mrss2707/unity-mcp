"""Tests for the NEW manage_input_system tool (Layer 2, group="input_system")."""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_input_system import manage_input_system


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
        "services.tools.manage_input_system.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_input_system.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── get_asset ──────────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_get_asset(mock_unity):
    """get_asset action should forward assetPath to Unity and return success."""
    result = await manage_input_system(
        SimpleNamespace(),
        action="get_asset",
        assetPath="Assets/TestInput.inputactions",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_input_system"
    assert mock_unity["params"]["action"] == "get_asset"
    assert mock_unity["params"]["assetPath"] == "Assets/TestInput.inputactions"


# ── create_asset ───────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_create_asset(mock_unity):
    """create_asset action should forward asset name and path to Unity."""
    result = await manage_input_system(
        SimpleNamespace(),
        action="create_asset",
        assetName="TestInput",
        assetPath="Assets/TestInput.inputactions",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_input_system"
    assert mock_unity["params"]["action"] == "create_asset"
    assert mock_unity["params"]["assetName"] == "TestInput"
    assert mock_unity["params"]["assetPath"] == "Assets/TestInput.inputactions"


# ── PACKAGE_MISSING error ──────────────────────────────────────────────


@pytest.mark.asyncio
async def test_package_missing_graceful(monkeypatch):
    """When Unity returns a PACKAGE_MISSING error, the tool should return it gracefully."""

    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "PACKAGE_MISSING: The Input System package is not installed.",
        }

    monkeypatch.setattr(
        "services.tools.manage_input_system.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_input_system.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_input_system(
        SimpleNamespace(),
        action="get_asset",
        assetPath="Assets/TestInput.inputactions",
    )
    assert result["success"] is False
    assert "message" in result
    assert "package_missing" in result["message"].lower() or "package" in result["message"].lower()
