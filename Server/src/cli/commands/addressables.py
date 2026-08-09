"""Addressables CLI commands."""

from typing import Optional

import click

from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output, print_info


@click.group()
def addressables():
    """Unity Addressables operations."""
    pass


@addressables.command("list-groups")
@handle_unity_errors
def list_groups():
    """List Addressables groups."""
    config = get_config()
    result = run_command("manage_addressables", {"action": "list_groups"}, config)
    click.echo(format_output(result, config.format))


@addressables.command("create-group")
@click.argument("name")
@click.option("--schema-type", default=None, help="Schema type for the group.")
@click.option("--build-path", default=None, help="Build path override.")
@click.option("--load-path", default=None, help="Load path override.")
@handle_unity_errors
def create_group(name: str, schema_type: Optional[str], build_path: Optional[str], load_path: Optional[str]):
    """Create an Addressables group."""
    config = get_config()
    params = {"action": "create_group", "groupName": name}
    if schema_type:
        params["schemaType"] = schema_type
    if build_path:
        params["buildPath"] = build_path
    if load_path:
        params["loadPath"] = load_path
    result = run_command("manage_addressables", params, config)
    click.echo(format_output(result, config.format))


@addressables.command("assign")
@click.argument("asset_path")
@click.argument("group_name")
@click.option("--address", default=None, help="Addressable address.")
@click.option("--label", "labels", multiple=True, help="Addressable label; repeatable.")
@handle_unity_errors
def assign(asset_path: str, group_name: str, address: Optional[str], labels: tuple[str, ...]):
    """Assign an asset to an Addressables group."""
    config = get_config()
    params = {"action": "assign_asset", "assetPath": asset_path, "groupName": group_name}
    if address:
        params["address"] = address
    if labels:
        params["labels"] = list(labels)
    result = run_command("manage_addressables", params, config)
    click.echo(format_output(result, config.format))


@addressables.command("remove")
@click.argument("asset_path")
@click.option("--group-name", default=None, help="Expected group name.")
@handle_unity_errors
def remove(asset_path: str, group_name: Optional[str]):
    """Remove an asset from Addressables."""
    config = get_config()
    params = {"action": "remove_asset", "assetPath": asset_path}
    if group_name:
        params["groupName"] = group_name
    result = run_command("manage_addressables", params, config)
    click.echo(format_output(result, config.format))


@addressables.command("build")
@click.option("--target-platform", type=click.Choice(["Android", "iOS", "StandaloneWindows64", "StandaloneOSX"]), default=None)
@handle_unity_errors
def build_content(target_platform: Optional[str]):
    """Build Addressables content."""
    config = get_config()
    params = {"action": "build_content"}
    if target_platform:
        params["targetPlatform"] = target_platform
    result = run_command("manage_addressables", params, config)
    click.echo(format_output(result, config.format))
    job_id = (result.get("data") or {}).get("job_id") if isinstance(result, dict) else None
    if job_id:
        print_info(f"Addressables build started: {job_id}")


@addressables.command("dependencies")
@click.argument("asset_path")
@handle_unity_errors
def dependencies(asset_path: str):
    """Inspect an Addressables dependency chain."""
    config = get_config()
    result = run_command("manage_addressables", {"action": "get_dependency_chain", "assetPath": asset_path}, config)
    click.echo(format_output(result, config.format))
