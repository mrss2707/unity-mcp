from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "create_group", "assign_asset", "remove_asset",
    "list_groups", "build_content", "get_dependency_chain",
]


@mcp_for_unity_tool(
    description="Manage Unity Addressables — create groups, assign assets, build content, inspect dependency chains",
    group="core",
    annotations=ToolAnnotations(title="Manage Addressables", destructiveHint=True),
)
async def manage_addressables(
    ctx: Context,
    action: Annotated[Literal[
        "create_group", "assign_asset", "remove_asset",
        "list_groups", "build_content", "get_dependency_chain",
    ], "The operation to perform."],
    groupName: Annotated[str | None, "Addressable group name."] = None,
    schemaType: Annotated[str | None, "Schema type for the new group."] = None,
    buildPath: Annotated[str | None, "Build path for the group."] = None,
    loadPath: Annotated[str | None, "Load path for the group."] = None,
    assetPath: Annotated[str | None, "Asset path to assign/remove."] = None,
    address: Annotated[str | None, "Addressable address for the asset."] = None,
    labels: Annotated[list[str] | None, "Labels for the addressable asset."] = None,
    targetPlatform: Annotated[Literal["Android", "iOS", "StandaloneWindows64", "StandaloneOSX"] | None, "Target build platform."] = None,
) -> dict[str, Any]:
    """Unified Addressables management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        return {
            "success": False,
            "message": (
                f"Unknown action '{action}'. Valid actions: "
                f"{', '.join(ALL_ACTIONS)}"
            ),
        }

    # Preflight for write actions (list_groups and get_dependency_chain are read-only)
    read_actions = {"list_groups", "get_dependency_chain"}
    if action_normalized not in read_actions:
        gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
        if gate is not None:
            return gate.model_dump()

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}

    if groupName is not None:
        params_dict["groupName"] = groupName
    if schemaType is not None:
        params_dict["schemaType"] = schemaType
    if buildPath is not None:
        params_dict["buildPath"] = buildPath
    if loadPath is not None:
        params_dict["loadPath"] = loadPath
    if assetPath is not None:
        params_dict["assetPath"] = assetPath
    if address is not None:
        params_dict["address"] = address
    if labels is not None:
        params_dict["labels"] = labels
    if targetPlatform is not None:
        params_dict["targetPlatform"] = targetPlatform

    # Remove any remaining None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # build_content uses the long-running-job dispatch pattern
    if action_normalized == "build_content":
        result = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_addressables",
            params_dict,
        )
        return result if isinstance(result, dict) else {"success": False, "message": str(result)}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_addressables",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
