"""Tests for the NEW manage_optimization tool (Layer 2, group="core")."""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_optimization import manage_optimization


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
        "services.tools.manage_optimization.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_optimization.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── set_quality_settings ───────────────────────────────────────────────


@pytest.mark.asyncio
async def test_set_quality_settings(mock_unity):
    """set_quality_settings action should forward preset and platform to Unity."""
    result = await manage_optimization(
        SimpleNamespace(),
        action="set_quality_settings",
        preset="medium",
        platform="StandaloneWindows64",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_optimization"
    assert mock_unity["params"]["action"] == "set_quality_settings"
    assert mock_unity["params"]["preset"] == "medium"
    assert mock_unity["params"]["platform"] == "StandaloneWindows64"


# ── INVALID_PATH error ─────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_batch_resize_textures_path_validation(monkeypatch):
    """When Unity returns an INVALID_PATH error, the tool should return it gracefully."""

    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "INVALID_PATH: The specified texture path 'Assets/!!Bad!!' does not exist.",
        }

    monkeypatch.setattr(
        "services.tools.manage_optimization.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_optimization.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_optimization(
        SimpleNamespace(),
        action="batch_resize_textures",
        path="Assets/!!Bad!!",
        maxWidth=512,
    )
    assert result["success"] is False
    assert "message" in result
    assert "invalid_path" in result["message"].lower() or "invalid" in result["message"].lower()


# ── analyze_build_size ─────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_analyze_build_size(mock_unity):
    """analyze_build_size action should forward to Unity and return success."""
    result = await manage_optimization(
        SimpleNamespace(),
        action="analyze_build_size",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "analyze_build_size"


# ── configure_texture_compression ──────────────────────────────────────


@pytest.mark.asyncio
async def test_configure_texture_compression(mock_unity):
    """configure_texture_compression action should forward platform and format to Unity."""
    result = await manage_optimization(
        SimpleNamespace(),
        action="configure_texture_compression",
        platform="Android",
        format="ASTC",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "configure_texture_compression"
    assert mock_unity["params"]["platform"] == "Android"
    assert mock_unity["params"]["format"] == "ASTC"
