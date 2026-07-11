"""Tests for the NEW manage_addressables tool (Layer 2, group="core")."""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_addressables import manage_addressables


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
        "services.tools.manage_addressables.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_addressables.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── PACKAGE_MISSING error ──────────────────────────────────────────────


@pytest.mark.asyncio
async def test_package_missing_graceful(monkeypatch):
    """When Unity returns a PACKAGE_MISSING error, the tool should return it gracefully."""

    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "PACKAGE_MISSING: The Addressables package is not installed.",
        }

    monkeypatch.setattr(
        "services.tools.manage_addressables.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_addressables.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_addressables(
        SimpleNamespace(),
        action="list_groups",
    )
    assert result["success"] is False
    assert "message" in result
    assert "package_missing" in result["message"].lower() or "package" in result["message"].lower()


# ── list_groups ────────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_list_groups(mock_unity):
    """list_groups action should forward to Unity and return success."""
    result = await manage_addressables(
        SimpleNamespace(),
        action="list_groups",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_addressables"
    assert mock_unity["params"]["action"] == "list_groups"


# ── create_group ───────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_create_group(mock_unity):
    """create_group action should forward groupName to Unity."""
    result = await manage_addressables(
        SimpleNamespace(),
        action="create_group",
        groupName="TestGroup",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "create_group"
    assert mock_unity["params"]["groupName"] == "TestGroup"


# ── assign_asset ───────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_assign_asset(mock_unity):
    """assign_asset action should forward asset path, group name, and address to Unity."""
    result = await manage_addressables(
        SimpleNamespace(),
        action="assign_asset",
        assetPath="Assets/Prefabs/test.prefab",
        groupName="TestGroup",
        address="test",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "assign_asset"
    assert mock_unity["params"]["assetPath"] == "Assets/Prefabs/test.prefab"
    assert mock_unity["params"]["groupName"] == "TestGroup"
    assert mock_unity["params"]["address"] == "test"
