from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight
from services.tools.utils import coerce_bool, coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "create_asset", "get_asset", "add_action_map", "remove_action_map",
    "add_action", "remove_action", "rename_action",
    "add_control_scheme", "remove_control_scheme",
    "add_bindings", "remove_bindings", "add_composite",
]


@mcp_for_unity_tool(
    group="input_system",
    description=(
        "Manage Unity Input System Action Assets — create, read, and modify "
        "InputActionAssets with action maps, bindings, control schemes, "
        "and composite bindings.\n\n"
        "ASSET OPERATIONS:\n"
        "- create_asset: Create a new InputActionAsset at the specified path\n"
        "- get_asset: Retrieve details of an existing InputActionAsset\n\n"
        "ACTION MAP OPERATIONS:\n"
        "- add_action_map: Add a new action map to an asset\n"
        "- remove_action_map: Remove an action map from an asset\n\n"
        "ACTION OPERATIONS:\n"
        "- add_action: Add a new action to an action map\n"
        "- remove_action: Remove an action from an action map\n"
        "- rename_action: Rename an existing action\n\n"
        "CONTROL SCHEME OPERATIONS:\n"
        "- add_control_scheme: Add a new control scheme to an asset\n"
        "- remove_control_scheme: Remove a control scheme from an asset\n\n"
        "BINDING OPERATIONS:\n"
        "- add_bindings: Add binding(s) to an action\n"
        "- remove_bindings: Remove binding(s) from an action\n"
        "- add_composite: Add a composite binding (1DAxis, 2DVector, etc.) to an action"
    ),
    annotations=ToolAnnotations(
        title="Manage Input System",
        destructiveHint=True,
        readOnlyHint=False,
    ),
)
async def manage_input_system(
    ctx: Context,
    action: Annotated[Literal[
        "create_asset", "get_asset", "add_action_map", "remove_action_map",
        "add_action", "remove_action", "rename_action",
        "add_control_scheme", "remove_control_scheme",
        "add_bindings", "remove_bindings", "add_composite",
    ], "The operation to perform on the Input System asset."],
    assetPath: Annotated[str | None, "Path to the InputActionAsset (.inputactions)."] = None,
    mapName: Annotated[str | None, "Action map name."] = None,
    actionName: Annotated[str | None, "Action name within an action map."] = None,
    actionType: Annotated[Literal["Button", "Value", "PassThrough"] | None, "Input action type."] = None,
    controlLayout: Annotated[Literal["Button", "Vector2", "Vector3", "Axis", "Key", "Stick", "Dpad", "Touch"] | None, "Expected control layout."] = None,
    binding: Annotated[str | None, "Single binding path (e.g. <Keyboard>/space)."] = None,
    bindings: Annotated[list[str] | None, "List of binding paths."] = None,
    interactions: Annotated[str | None, "Interactions string (e.g. Hold, Press)."] = None,
    processors: Annotated[str | None, "Processors string (e.g. Normalize)."] = None,
    groups: Annotated[str | None, "Control scheme groups (comma-separated)."] = None,
    schemeName: Annotated[str | None, "Control scheme name."] = None,
    requiredDevices: Annotated[list[str] | None, "Required devices for control scheme."] = None,
    optionalDevices: Annotated[list[str] | None, "Optional devices for control scheme."] = None,
    oldName: Annotated[str | None, "Current action name (for rename)."] = None,
    newName: Annotated[str | None, "New action name (for rename)."] = None,
    compositeType: Annotated[Literal["1DAxis", "2DVector", "3DVector", "Dpad", "Stick"] | None, "Composite binding type."] = None,
    compositeName: Annotated[str | None, "Name for the composite binding. Defaults to compositeType."] = None,
    parts: Annotated[dict[str, str] | None, "Composite parts as part-name to binding path, e.g. {\"up\": \"<Keyboard>/w\", \"down\": \"<Keyboard>/s\"}. Required for add_composite."] = None,
    page_size: Annotated[int | str | None, "Number of action maps to return for get_asset."] = None,
    cursor: Annotated[int | str | None, "Zero-based action map cursor for get_asset."] = None,
    include_json: Annotated[bool | str | None, "Include a bounded JSON chunk for get_asset."] = None,
    json_cursor: Annotated[int | str | None, "Zero-based JSON character cursor for get_asset."] = None,
    json_chunk_size: Annotated[int | str | None, "Maximum JSON characters to return per get_asset chunk."] = None,
) -> dict[str, Any]:
    """Unified Input System management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        return {
            "success": False,
            "message": (
                f"Unknown action '{action}'. Valid actions: "
                f"{', '.join(ALL_ACTIONS)}"
            ),
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    if action_normalized != "get_asset":
        gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
        if gate is not None:
            return gate.model_dump()

    params_dict: dict[str, Any] = {"action": action_normalized}

    # Asset parameters
    if assetPath is not None:
        params_dict["assetPath"] = assetPath

    # Action map parameters
    if mapName is not None:
        params_dict["mapName"] = mapName

    # Action parameters
    if actionName is not None:
        params_dict["actionName"] = actionName
    if actionType is not None:
        params_dict["actionType"] = actionType
    if controlLayout is not None:
        params_dict["controlLayout"] = controlLayout

    # Binding parameters
    if binding is not None:
        params_dict["binding"] = binding
    if bindings is not None:
        params_dict["bindings"] = bindings
    if interactions is not None:
        params_dict["interactions"] = interactions
    if processors is not None:
        params_dict["processors"] = processors
    if groups is not None:
        params_dict["groups"] = groups

    # Control scheme parameters
    if schemeName is not None:
        params_dict["schemeName"] = schemeName
    if requiredDevices is not None:
        params_dict["requiredDevices"] = requiredDevices
    if optionalDevices is not None:
        params_dict["optionalDevices"] = optionalDevices

    # Rename parameters
    if oldName is not None:
        params_dict["oldName"] = oldName
    if newName is not None:
        params_dict["newName"] = newName

    # Composite parameters
    if compositeType is not None:
        params_dict["compositeType"] = compositeType
    if compositeName is not None:
        params_dict["compositeName"] = compositeName
    if parts is not None:
        params_dict["parts"] = parts

    if page_size is not None:
        params_dict["pageSize"] = coerce_int(page_size, default=50)
    if cursor is not None:
        params_dict["cursor"] = coerce_int(cursor, default=0)
    include_json_value = coerce_bool(include_json, default=None)
    if include_json_value is not None:
        params_dict["includeJson"] = include_json_value
    if json_cursor is not None:
        params_dict["jsonCursor"] = coerce_int(json_cursor, default=0)
    if json_chunk_size is not None:
        params_dict["jsonChunkSize"] = coerce_int(json_chunk_size, default=8192)

    # Remove any remaining None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_input_system",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
