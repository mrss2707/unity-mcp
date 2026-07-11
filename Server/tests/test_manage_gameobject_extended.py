"""Tests for Layer 1 extended actions on manage_gameobject."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_gameobject import manage_gameobject


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
        "services.tools.manage_gameobject.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_gameobject.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── set_sibling_index ────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_set_sibling_index(mock_unity):
    """set_sibling_index action should forward params to Unity and return success."""
    result = await manage_gameobject(
        SimpleNamespace(),
        action="set_sibling_index",
        target="MainCamera",
        index=0,
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_gameobject"
    assert mock_unity["params"]["action"] == "set_sibling_index"


# ── get_detailed_info ────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_get_detailed_info(mock_unity):
    """get_detailed_info action should forward to Unity and return success."""
    result = await manage_gameobject(
        SimpleNamespace(),
        action="get_detailed_info",
        target="MainCamera",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "get_detailed_info"


@pytest.mark.asyncio
async def test_get_detailed_info_not_found(monkeypatch):
    """get_detailed_info on an invalid path should return an error response."""
    captured: dict[str, object] = {}

    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {
            "success": False,
            "message": "GameObject not found: NonexistentObject",
        }

    monkeypatch.setattr(
        "services.tools.manage_gameobject.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_gameobject.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_gameobject(
        SimpleNamespace(),
        action="get_detailed_info",
        target="NonexistentObject",
    )
    assert result["success"] is False
    assert "message" in result
    assert "not found" in result["message"].lower()
