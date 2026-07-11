"""
Tool for managing components on GameObjects in Unity.
Supports add, remove, and set_property operations.
"""
from typing import Annotated, Any, Literal, Optional

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import parse_json_payload, normalize_properties
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description=(
        "Add, remove, or set properties on components attached to GameObjects. "
        "Actions: add, remove, set_property, get_property, list_all, add_simple_listener, add_param_listener, remove_listener, get_listeners. "
        "Requires target (instance ID or name) and component_type for add/remove/set_property. "
        "Use gameObjectPath for inspection/listener actions. "
        "For READING component data, use the mcpforunity://scene/gameobject/{id}/components resource "
        "or mcpforunity://scene/gameobject/{id}/component/{name} for a single component. "
        "For creating/deleting GameObjects themselves, use manage_gameobject instead."
    )
)
async def manage_components(
    ctx: Context,
    action: Annotated[
        Literal["add", "remove", "set_property", "get_property", "list_all",
                "add_simple_listener", "add_param_listener", "remove_listener", "get_listeners"],
        "Action to perform: add, remove, set_property, get_property, list_all, add_simple_listener, add_param_listener, remove_listener, get_listeners"
    ],
    target: Annotated[
        str | int | None,
        "Target GameObject - instance ID (preferred) or name/path. Required for add/remove/set_property."
    ] = None,
    component_type: Annotated[
        str | None,
        "Component type name (e.g., 'Rigidbody', 'BoxCollider', 'MyScript'). Required for add/remove/set_property/get_property."
    ] = None,
    search_method: Annotated[
        Optional[Literal["by_id", "by_name", "by_path"]],
        "How to find the target GameObject"
    ] = None,
    # For set_property action - single property
    property: Annotated[Optional[str],
                        "Property name to set (for set_property action)"] = None,
    value: Annotated[Optional[str | int | float | bool | dict | list],
                     "Value to set (for set_property action). "
                     "For object references: instance ID (int), asset path (string), "
                     "or {\"guid\": \"...\"} / {\"path\": \"...\"}. "
                     "For Sprite sub-assets: {\"guid\": \"...\", \"spriteName\": \"<name>\"} or "
                     "{\"guid\": \"...\", \"fileID\": <id>}. Single-sprite textures auto-resolve."] = None,
    # For add/set_property - multiple properties
    properties: Annotated[
        Optional[dict[str, Any] | str],
        "Dictionary of property names to values. Example: {\"mass\": 5.0, \"useGravity\": false}"
    ] = None,
    # For targeting a specific component when multiple of the same type exist
    component_index: Annotated[
        Optional[int],
        "Zero-based index to select which component when multiple of the same type exist. "
        "Use the components resource to discover indices. If omitted, targets the first instance."
    ] = None,
    # --- Parameters for new inspection/listener actions ---
    gameObjectPath: Annotated[Optional[str], "Path to the target GameObject (for inspection/listener actions)."] = None,
    propertyName: Annotated[Optional[str], "Name of the property to get (for get_property action)."] = None,
    eventName: Annotated[Optional[str], "Name of the UnityEvent field (for listener actions)."] = None,
    targetPath: Annotated[Optional[str], "Path to the target GameObject for the listener callback."] = None,
    methodName: Annotated[Optional[str], "Method name for the listener callback."] = None,
    paramType: Annotated[Optional[Literal["int", "float", "string", "bool", "Object"]], "Parameter type for typed listener."] = None,
    paramValue: Annotated[Optional[str], "Parameter value for typed listener (as string, will be parsed by C#)."] = None,
    listenerIndex: Annotated[Optional[int], "Index of the persistent listener to remove."] = None,
) -> dict[str, Any]:
    """
    Manage components on GameObjects.

    Actions:
    - add: Add a new component to a GameObject
    - remove: Remove a component from a GameObject  
    - set_property: Set one or more properties on a component

    Examples:
    - Add Rigidbody: action="add", target="Player", component_type="Rigidbody"
    - Remove BoxCollider: action="remove", target=-12345, component_type="BoxCollider"
    - Set single property: action="set_property", target="Enemy", component_type="Rigidbody", property="mass", value=5.0
    - Set multiple properties: action="set_property", target="Enemy", component_type="Rigidbody", properties={"mass": 5.0, "useGravity": false}
    """
    unity_instance = await get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    if not action:
        return {
            "success": False,
            "message": "Missing required parameter 'action'. Valid actions: add, remove, set_property, get_property, list_all, add_simple_listener, add_param_listener, remove_listener, get_listeners"
        }

    legacy_actions = {"add", "remove", "set_property"}
    if action in legacy_actions:
        if not target:
            return {
                "success": False,
                "message": "Missing required parameter 'target'. Specify GameObject instance ID or name."
            }
        if not component_type:
            return {
                "success": False,
                "message": "Missing required parameter 'component_type'. Specify the component type name."
            }

    # --- Normalize properties with detailed error handling ---
    properties, props_error = normalize_properties(properties)
    if props_error:
        return {"success": False, "message": props_error}

    # --- Validate value parameter for serialization issues ---
    if value is not None and isinstance(value, str) and value in ("[object Object]", "undefined"):
        return {"success": False, "message": f"value received invalid input: '{value}'. Expected an actual value."}

    try:
        params = {
            "action": action,
            "target": target,
            "componentType": component_type,
            "gameObjectPath": gameObjectPath,
            "propertyName": propertyName,
            "eventName": eventName,
            "targetPath": targetPath,
            "methodName": methodName,
            "paramType": paramType,
            "paramValue": paramValue,
            "listenerIndex": listenerIndex,
        }

        if search_method:
            params["searchMethod"] = search_method

        if component_index is not None:
            params["componentIndex"] = component_index

        if action == "set_property":
            if property and value is not None:
                params["property"] = property
                params["value"] = value
            if properties:
                params["properties"] = properties

        if action == "add" and properties:
            params["properties"] = properties

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_components",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", f"Component {action} successful."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error managing component: {e!s}"}
