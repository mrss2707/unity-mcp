"""Tests for the NEW manage_audio tool (Layer 2, group="core")."""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_audio import manage_audio


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
        "services.tools.manage_audio.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_audio.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── create_source ──────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_create_source(mock_unity):
    """create_source action should forward audio source params to Unity."""
    result = await manage_audio(
        SimpleNamespace(),
        action="create_source",
        gameObjectPath="TestObject",
        clipPath="Assets/Audio/test.wav",
        volume=0.8,
        loop=True,
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_audio"
    assert mock_unity["params"]["action"] == "create_source"
    assert mock_unity["params"]["gameObjectPath"] == "TestObject"
    assert mock_unity["params"]["clipPath"] == "Assets/Audio/test.wav"
    assert mock_unity["params"]["volume"] == 0.8
    assert mock_unity["params"]["loop"] is True


# ── play edit-mode error ───────────────────────────────────────────────


@pytest.mark.asyncio
async def test_play_edit_mode_error(monkeypatch):
    """When Unity returns EDIT_MODE_NOT_SUPPORTED for play action, the tool should return it gracefully."""

    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "EDIT_MODE_NOT_SUPPORTED: Audio playback is only available in Play Mode.",
        }

    monkeypatch.setattr(
        "services.tools.manage_audio.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_audio.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_audio(
        SimpleNamespace(),
        action="play",
        gameObjectPath="TestObject",
    )
    assert result["success"] is False
    assert "message" in result
    assert "edit_mode" in result["message"].lower() or "play mode" in result["message"].lower()


# ── set_source ─────────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_set_source(mock_unity):
    """set_source action should forward spatial blend and pitch to Unity."""
    result = await manage_audio(
        SimpleNamespace(),
        action="set_source",
        gameObjectPath="TestObject",
        spatialBlend=0.5,
        pitch=1.2,
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "set_source"
    assert mock_unity["params"]["gameObjectPath"] == "TestObject"
    assert mock_unity["params"]["spatialBlend"] == 0.5
    assert mock_unity["params"]["pitch"] == 1.2


# ── configure_spatial ──────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_configure_spatial(mock_unity):
    """configure_spatial action should forward spatial audio params to Unity."""
    result = await manage_audio(
        SimpleNamespace(),
        action="configure_spatial",
        gameObjectPath="TestObject",
        spatialBlend=1.0,
        minDistance=1,
        maxDistance=50,
        rolloffMode="Logarithmic",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "configure_spatial"
    assert mock_unity["params"]["gameObjectPath"] == "TestObject"
    assert mock_unity["params"]["spatialBlend"] == 1.0
    assert mock_unity["params"]["minDistance"] == 1
    assert mock_unity["params"]["maxDistance"] == 50
    assert mock_unity["params"]["rolloffMode"] == "Logarithmic"
