"""Tests for Layer 1 extended actions on manage_editor."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_editor import manage_editor


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
        "services.tools.manage_editor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_editor.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── create_folder_structure ──────────────────────────────────────────


@pytest.mark.asyncio
async def test_create_folder_structure_default(mock_unity):
    """create_folder_structure with default structure should forward to Unity."""
    result = await manage_editor(
        SimpleNamespace(),
        action="create_folder_structure",
        structure="default",
        rootPath="Assets/TestMCP",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_editor"
    assert mock_unity["params"]["action"] == "create_folder_structure"
    assert mock_unity["params"]["structure"] == "default"
    assert mock_unity["params"]["rootPath"] == "Assets/TestMCP"


# ── run_health_check ──────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_run_health_check_compile(mock_unity):
    """run_health_check with compile check should forward to Unity."""
    result = await manage_editor(
        SimpleNamespace(),
        action="run_health_check",
        checks=["compile"],
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "run_health_check"
    assert mock_unity["params"]["checks"] == ["compile"]


# ── Error cases ──────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_create_folder_structure_invalid_path(monkeypatch):
    """create_folder_structure with an invalid rootPath should return INVALID_PATH error."""
    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "INVALID_PATH: The specified root path 'Assets/!!Invalid!!' is not valid.",
        }

    monkeypatch.setattr(
        "services.tools.manage_editor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_editor.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_editor(
        SimpleNamespace(),
        action="create_folder_structure",
        structure="default",
        rootPath="Assets/!!Invalid!!",
    )
    assert result["success"] is False
    assert "message" in result
    assert "invalid_path" in result["message"].lower() or "invalid" in result["message"].lower()
