"""Tests for Layer 1 extended actions on manage_profiler."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_profiler import (
    manage_profiler,
    ALL_ACTIONS,
)


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
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── get_draw_calls ───────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_get_draw_calls(mock_unity):
    """get_draw_calls is in ALL_ACTIONS; should forward to Unity and succeed."""
    result = await manage_profiler(
        SimpleNamespace(),
        action="get_draw_calls",
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_draw_calls"



# ── get_gpu_profile ──────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_get_gpu_profile(mock_unity):
    """get_gpu_profile is in ALL_ACTIONS; should forward to Unity and succeed."""
    result = await manage_profiler(
        SimpleNamespace(),
        action="get_gpu_profile",
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_gpu_profile"


# ── profiler_not_connected ───────────────────────────────────────────


@pytest.mark.asyncio
async def test_profiler_not_connected(monkeypatch):
    """When the profiler has not been started, actions should return a not-connected error."""
    async def fake_send_not_connected(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "Profiler is not connected. Start the profiler with profiler_start first.",
        }

    monkeypatch.setattr(
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send_not_connected,
    )

    # Use an already-supported action to test the not-connected scenario
    result = await manage_profiler(
        SimpleNamespace(),
        action="get_frame_timing",
    )
    assert result["success"] is False
    assert "message" in result
    assert "not connected" in result["message"].lower()
