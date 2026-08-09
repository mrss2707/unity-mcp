"""Input System CLI commands."""

from typing import Optional

import click

from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output
from cli.utils.parsers import parse_json_dict_or_exit


@click.group(name="input-system")
def input_system():
    """Unity Input System Action Asset operations."""
    pass


@input_system.command("create")
@click.argument("asset_path")
@click.option("--map-name", default=None, help="Initial action map name.")
@handle_unity_errors
def create(asset_path: str, map_name: Optional[str]):
    """Create an InputActionAsset."""
    config = get_config()
    params = {"action": "create_asset", "assetPath": asset_path}
    if map_name:
        params["mapName"] = map_name
    result = run_command("manage_input_system", params, config)
    click.echo(format_output(result, config.format))


@input_system.command("get")
@click.argument("asset_path")
@click.option("--page-size", type=int, default=None, help="Number of action maps to return.")
@click.option("--cursor", type=int, default=None, help="Zero-based action map cursor.")
@click.option("--include-json", is_flag=True, help="Include a bounded JSON chunk.")
@click.option("--json-cursor", type=int, default=None, help="JSON chunk cursor.")
@click.option("--json-chunk-size", type=int, default=None, help="JSON chunk size.")
@handle_unity_errors
def get(asset_path: str, page_size: Optional[int], cursor: Optional[int], include_json: bool, json_cursor: Optional[int], json_chunk_size: Optional[int]):
    """Read a bounded summary of an InputActionAsset."""
    config = get_config()
    params = {"action": "get_asset", "assetPath": asset_path}
    if page_size is not None:
        params["pageSize"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    if include_json:
        params["includeJson"] = True
    if json_cursor is not None:
        params["jsonCursor"] = json_cursor
    if json_chunk_size is not None:
        params["jsonChunkSize"] = json_chunk_size
    result = run_command("manage_input_system", params, config)
    click.echo(format_output(result, config.format))


@input_system.command("add-map")
@click.argument("asset_path")
@click.argument("map_name")
@handle_unity_errors
def add_map(asset_path: str, map_name: str):
    """Add an action map."""
    config = get_config()
    result = run_command("manage_input_system", {"action": "add_action_map", "assetPath": asset_path, "mapName": map_name}, config)
    click.echo(format_output(result, config.format))


@input_system.command("add-action")
@click.argument("asset_path")
@click.argument("map_name")
@click.argument("action_name")
@click.option("--action-type", type=click.Choice(["Button", "Value", "PassThrough"]), default=None)
@click.option("--control-layout", default=None, help="Expected control layout.")
@handle_unity_errors
def add_action(asset_path: str, map_name: str, action_name: str, action_type: Optional[str], control_layout: Optional[str]):
    """Add an action to a map."""
    config = get_config()
    params = {"action": "add_action", "assetPath": asset_path, "mapName": map_name, "actionName": action_name}
    if action_type:
        params["actionType"] = action_type
    if control_layout:
        params["controlLayout"] = control_layout
    result = run_command("manage_input_system", params, config)
    click.echo(format_output(result, config.format))


@input_system.command("add-bindings")
@click.argument("asset_path")
@click.argument("map_name")
@click.argument("action_name")
@click.option("--binding", "bindings", multiple=True, help="Binding path; repeatable.")
@click.option("--groups", default=None)
@click.option("--interactions", default=None)
@click.option("--processors", default=None)
@handle_unity_errors
def add_bindings(asset_path: str, map_name: str, action_name: str, bindings: tuple[str, ...], groups: Optional[str], interactions: Optional[str], processors: Optional[str]):
    """Add one or more bindings to an action."""
    config = get_config()
    params = {"action": "add_bindings", "assetPath": asset_path, "mapName": map_name, "actionName": action_name}
    if bindings:
        params["bindings"] = list(bindings)
    if groups:
        params["groups"] = groups
    if interactions:
        params["interactions"] = interactions
    if processors:
        params["processors"] = processors
    result = run_command("manage_input_system", params, config)
    click.echo(format_output(result, config.format))


@input_system.command("add-composite")
@click.argument("asset_path")
@click.argument("map_name")
@click.argument("action_name")
@click.argument("composite_type")
@click.option("--parts", required=True, help='Composite parts JSON, e.g. {"up":"<Keyboard>/w"}.')
@handle_unity_errors
def add_composite(asset_path: str, map_name: str, action_name: str, composite_type: str, parts: str):
    """Add a composite binding to an action."""
    config = get_config()
    params = {
        "action": "add_composite",
        "assetPath": asset_path,
        "mapName": map_name,
        "actionName": action_name,
        "compositeType": composite_type,
        "parts": parse_json_dict_or_exit(parts, "parts"),
    }
    result = run_command("manage_input_system", params, config)
    click.echo(format_output(result, config.format))
