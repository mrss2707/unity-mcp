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
    "create_source", "set_source", "play", "stop",
    "create_mixer", "expose_param", "set_snapshot", "configure_spatial",
]


@mcp_for_unity_tool(
    description="Manage Unity Audio — create and configure AudioSources, Audio Mixers, mixer snapshots, and 3D spatial audio settings",
    group="core",
    annotations=ToolAnnotations(title="Manage Audio"),
)
async def manage_audio(
    ctx: Context,
    action: Annotated[Literal[
        "create_source", "set_source", "play", "stop",
        "create_mixer", "expose_param", "set_snapshot", "configure_spatial"
    ], "The operation to perform."],
    gameObjectPath: Annotated[str | None, "Path to the target GameObject."] = None,
    clipPath: Annotated[str | None, "Path to the AudioClip asset."] = None,
    playOnAwake: Annotated[bool | None, "Whether audio plays on awake."] = None,
    loop: Annotated[bool | None, "Whether audio loops."] = None,
    volume: Annotated[float | None, "Audio volume (0.0 to 1.0)."] = None,
    pitch: Annotated[float | None, "Audio pitch (default: 1.0)."] = None,
    spatialBlend: Annotated[float | None, "Spatial blend (0.0 = 2D, 1.0 = 3D)."] = None,
    minDistance: Annotated[float | None, "3D sound min distance."] = None,
    maxDistance: Annotated[float | None, "3D sound max distance."] = None,
    rolloffMode: Annotated[Literal["Logarithmic", "Linear", "Custom"] | None, "Audio rolloff mode."] = None,
    dopplerLevel: Annotated[float | None, "Doppler effect level (0.0 to 5.0)."] = None,
    mixerName: Annotated[str | None, "AudioMixer asset name."] = None,
    outputPath: Annotated[str | None, "Output path for new AudioMixer asset."] = None,
    paramName: Annotated[str | None, "Exposed parameter name."] = None,
    groupId: Annotated[str | None, "Mixer group ID or path."] = None,
    snapshotName: Annotated[str | None, "Snapshot name."] = None,
    fadeTime: Annotated[float | None, "Fade transition time in seconds."] = None,
) -> dict[str, Any]:
    """Unified Audio management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        return {
            "success": False,
            "message": (
                f"Unknown action '{action}'. Valid actions: "
                f"{', '.join(ALL_ACTIONS)}"
            ),
        }

    # Preflight for write actions that modify scene/project state
    write_actions = {"create_source", "set_source", "create_mixer", "expose_param", "configure_spatial"}
    if action_normalized in write_actions:
        gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
        if gate is not None:
            return gate.model_dump()

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}

    # Target GameObject
    if gameObjectPath is not None:
        params_dict["gameObjectPath"] = gameObjectPath

    # AudioClip
    if clipPath is not None:
        params_dict["clipPath"] = clipPath

    # AudioSource settings
    if playOnAwake is not None:
        params_dict["playOnAwake"] = coerce_bool(playOnAwake)
    if loop is not None:
        params_dict["loop"] = coerce_bool(loop)
    if volume is not None:
        params_dict["volume"] = volume
    if pitch is not None:
        params_dict["pitch"] = pitch

    # 3D spatial audio settings
    if spatialBlend is not None:
        params_dict["spatialBlend"] = spatialBlend
    if minDistance is not None:
        params_dict["minDistance"] = minDistance
    if maxDistance is not None:
        params_dict["maxDistance"] = maxDistance
    if rolloffMode is not None:
        params_dict["rolloffMode"] = rolloffMode
    if dopplerLevel is not None:
        params_dict["dopplerLevel"] = dopplerLevel

    # AudioMixer parameters
    if mixerName is not None:
        params_dict["mixerName"] = mixerName
    if outputPath is not None:
        params_dict["outputPath"] = outputPath
    if paramName is not None:
        params_dict["paramName"] = paramName
    if groupId is not None:
        params_dict["groupId"] = groupId

    # Snapshot parameters
    if snapshotName is not None:
        params_dict["snapshotName"] = snapshotName
    if fadeTime is not None:
        params_dict["fadeTime"] = fadeTime

    # Remove any remaining None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_audio",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
