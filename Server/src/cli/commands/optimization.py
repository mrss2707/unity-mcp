"""Optimization CLI commands."""

from typing import Optional

import click

from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output
from cli.utils.parsers import parse_json_dict_or_exit


@click.group()
def optimization():
    """Cross-platform optimization operations."""
    pass


@optimization.command("quality")
@click.argument("preset", type=click.Choice(["low", "medium", "high", "ultra"]))
@click.option("--platform", type=click.Choice(["Android", "iOS", "StandaloneWindows64", "StandaloneOSX"]), default=None)
@handle_unity_errors
def quality(preset: str, platform: Optional[str]):
    """Set quality settings."""
    config = get_config()
    params = {"action": "set_quality_settings", "preset": preset}
    if platform:
        params["platform"] = platform
    result = run_command("manage_optimization", params, config)
    click.echo(format_output(result, config.format))


@optimization.command("texture-compression")
@click.argument("path")
@click.argument("format_name", type=click.Choice(["ASTC", "ETC2", "PVRTC", "DXT5"]))
@click.option("--platform", type=click.Choice(["Android", "iOS", "StandaloneWindows64", "StandaloneOSX"]), default=None)
@handle_unity_errors
def texture_compression(path: str, format_name: str, platform: Optional[str]):
    """Configure texture compression."""
    config = get_config()
    params = {"action": "configure_texture_compression", "path": path, "format": format_name}
    if platform:
        params["platform"] = platform
    result = run_command("manage_optimization", params, config)
    click.echo(format_output(result, config.format))


@optimization.command("resize-textures")
@click.argument("path")
@click.option("--max-width", type=int, default=None)
@click.option("--max-height", type=int, default=None)
@click.option("--filter", "filter_mode", type=click.Choice(["Point", "Bilinear", "Trilinear"]), default=None)
@handle_unity_errors
def resize_textures(path: str, max_width: Optional[int], max_height: Optional[int], filter_mode: Optional[str]):
    """Batch resize textures under a path."""
    config = get_config()
    params = {"action": "batch_resize_textures", "path": path}
    if max_width is not None:
        params["maxWidth"] = max_width
    if max_height is not None:
        params["maxHeight"] = max_height
    if filter_mode:
        params["filter"] = filter_mode
    result = run_command("manage_optimization", params, config)
    click.echo(format_output(result, config.format))


@optimization.command("sprite-atlas")
@click.argument("atlas_name")
@click.option("--include-path", "include_paths", multiple=True, help="Asset folder/path; repeatable.")
@click.option("--packing-settings", default=None, help="Packing settings JSON.")
@handle_unity_errors
def sprite_atlas(atlas_name: str, include_paths: tuple[str, ...], packing_settings: Optional[str]):
    """Create or update a sprite atlas."""
    config = get_config()
    params = {"action": "set_sprite_atlas", "atlasName": atlas_name}
    if include_paths:
        params["includePaths"] = list(include_paths)
    if packing_settings:
        params["packingSettings"] = parse_json_dict_or_exit(packing_settings, "packing-settings")
    result = run_command("manage_optimization", params, config)
    click.echo(format_output(result, config.format))


@optimization.command("build-size")
@click.option("--platform", type=click.Choice(["Android", "iOS", "StandaloneWindows64", "StandaloneOSX"]), default=None)
@handle_unity_errors
def build_size(platform: Optional[str]):
    """Analyze build size."""
    config = get_config()
    params = {"action": "analyze_build_size"}
    if platform:
        params["platform"] = platform
    result = run_command("manage_optimization", params, config)
    click.echo(format_output(result, config.format))
