"""Tests for Layer 1 extended actions on manage_components."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_components import manage_components


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
        "services.tools.manage_components.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_components.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── get_property ─────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_get_property(mock_unity):
    """get_property action should forward component and property info to Unity."""
    result = await manage_components(
        SimpleNamespace(),
        action="get_property",
        target="MainCamera",
        component_type="Transform",
        propertyName="m_LocalPosition",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["tool_name"] == "manage_components"
    assert mock_unity["params"]["action"] == "get_property"
    assert mock_unity["params"]["componentType"] == "Transform"
    assert mock_unity["params"]["propertyName"] == "m_LocalPosition"


# ── list_all ─────────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_list_all(mock_unity):
    """list_all action should forward to Unity and return all components."""
    result = await manage_components(
        SimpleNamespace(),
        action="list_all",
        target="MainCamera",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "list_all"
    assert mock_unity["params"]["target"] == "MainCamera"


# ── add_simple_listener ──────────────────────────────────────────────


@pytest.mark.asyncio
async def test_add_simple_listener(mock_unity):
    """add_simple_listener action should forward listener details to Unity."""
    result = await manage_components(
        SimpleNamespace(),
        action="add_simple_listener",
        target="MainCamera",
        component_type="SomeScript",
        eventName="OnSomeEvent",
        methodName="HandleSomeEvent",
        targetPath="EventManager",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "add_simple_listener"
    assert mock_unity["params"]["componentType"] == "SomeScript"
    assert mock_unity["params"]["eventName"] == "OnSomeEvent"


# ── get_listeners ────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_get_listeners(mock_unity):
    """get_listeners action should forward to Unity and return listener list."""
    result = await manage_components(
        SimpleNamespace(),
        action="get_listeners",
        target="MainCamera",
        component_type="SomeScript",
        eventName="OnSomeEvent",
    )
    assert result["success"] is True
    assert "message" in result
    assert mock_unity["params"]["action"] == "get_listeners"
    assert mock_unity["params"]["componentType"] == "SomeScript"


# ── Error cases ──────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_component_not_found(monkeypatch):
    """Invalid component_type should return an error from Unity."""
    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "Component 'NonExistentComponent' not found on target 'MainCamera'",
        }

    monkeypatch.setattr(
        "services.tools.manage_components.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_components.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_components(
        SimpleNamespace(),
        action="get_property",
        target="MainCamera",
        component_type="NonExistentComponent",
        property="someField",
    )
    assert result["success"] is False
    assert "message" in result
    assert "not found" in result["message"].lower()


@pytest.mark.asyncio
async def test_event_not_found(monkeypatch):
    """Invalid event_name should return an error from Unity."""
    async def fake_send_error(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "message": "Event 'NonExistentEvent' not found on component 'SomeScript'",
        }

    monkeypatch.setattr(
        "services.tools.manage_components.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_components.send_with_unity_instance",
        fake_send_error,
    )

    result = await manage_components(
        SimpleNamespace(),
        action="add_simple_listener",
        target="MainCamera",
        component_type="SomeScript",
        eventName="NonExistentEvent",
        methodName="HandleIt",
        targetPath="EventManager",
    )
    assert result["success"] is False
    assert "message" in result
    assert "not found" in result["message"].lower()
