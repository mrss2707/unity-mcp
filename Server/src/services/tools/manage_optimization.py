from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight
from services.tools.utils import coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "set_quality_settings", "configure_texture_compression",
    "batch_resize_textures", "set_sprite_atlas",
    "configure_lightmap", "analyze_build_size", "configure_occlusion",
]


@mcp_for_unity_tool(
    description="Manage Cross-platform Optimization — quality settings, texture compression, sprite atlases, lightmaps, occlusion culling, build size analysis",
    group="core",
    annotations=ToolAnnotations(title="Manage Optimization"),
)
async def manage_optimization(
    ctx: Context,
    action: Annotated[Literal[
        "set_quality_settings", "configure_texture_compression",
        "batch_resize_textures", "set_sprite_atlas",
        "configure_lightmap", "analyze_build_size", "configure_occlusion",
    ], "The operation to perform."],
    preset: Annotated[Literal["low", "medium", "high", "ultra"] | None, "Quality preset level for set_quality_settings."] = None,
    platform: Annotated[Literal["Android", "iOS", "StandaloneWindows64", "StandaloneOSX"] | None, "Target platform."] = None,
    format: Annotated[Literal["ASTC", "ETC2", "PVRTC", "DXT5"] | None, "Texture compression format."] = None,
    maxWidth: Annotated[int | None, "Maximum texture width for batch resize."] = None,
    maxHeight: Annotated[int | None, "Maximum texture height for batch resize."] = None,
    filter: Annotated[Literal["Point", "Bilinear", "Trilinear"] | None, "Resize filter mode."] = None,
    path: Annotated[str | None, "Target asset path for batch operations."] = None,
    atlasName: Annotated[str | None, "Sprite atlas asset name."] = None,
    includePaths: Annotated[list[str] | None, "Paths to include in the sprite atlas."] = None,
    packingSettings: Annotated[dict | None, "Sprite atlas packing settings."] = None,
    lightmapSize: Annotated[int | None, "Lightmap resolution size."] = None,
    compression: Annotated[Literal["None", "Low", "Normal", "High"] | None, "Lightmap compression level."] = None,
    realtimeGI: Annotated[bool | None, "Enable real-time global illumination."] = None,
    bakeSettings: Annotated[dict | None, "Lightmap bake settings."] = None,
    cullingMask: Annotated[int | None, "Occlusion culling layer mask."] = None,
) -> dict[str, Any]:
    """Unified Optimization management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        return {
            "success": False,
            "message": (
                f"Unknown action '{action}'. Valid actions: "
                f"{', '.join(ALL_ACTIONS)}"
            ),
        }

    # Preflight only for write actions (analyze_build_size is read-only)
    read_actions = {"analyze_build_size"}
    if action_normalized not in read_actions:
        gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
        if gate is not None:
            return gate.model_dump()

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}

    # Quality settings
    if preset is not None:
        params_dict["preset"] = preset

    # Platform / format
    if platform is not None:
        params_dict["platform"] = platform
    if format is not None:
        params_dict["format"] = format

    # Texture resize
    if maxWidth is not None:
        params_dict["maxWidth"] = maxWidth
    if maxHeight is not None:
        params_dict["maxHeight"] = maxHeight
    if filter is not None:
        params_dict["filter"] = filter
    if path is not None:
        params_dict["path"] = path

    # Sprite atlas
    if atlasName is not None:
        params_dict["atlasName"] = atlasName
    if includePaths is not None:
        params_dict["includePaths"] = includePaths
    if packingSettings is not None:
        params_dict["packingSettings"] = packingSettings

    # Lightmap
    if lightmapSize is not None:
        params_dict["lightmapSize"] = lightmapSize
    if compression is not None:
        params_dict["compression"] = compression
    if realtimeGI is not None:
        params_dict["realtimeGI"] = coerce_bool(realtimeGI)
    if bakeSettings is not None:
        params_dict["bakeSettings"] = bakeSettings

    # Occlusion
    if cullingMask is not None:
        params_dict["cullingMask"] = cullingMask

    # Remove any remaining None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_optimization",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
