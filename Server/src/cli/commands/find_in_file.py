"""Find-in-file CLI commands."""

from typing import Optional

import click

from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output


@click.group(name="find-in-file")
def find_in_file():
    """Search files and find symbol references."""
    pass


@find_in_file.command("search")
@click.argument("uri")
@click.argument("pattern")
@click.option("--max-results", type=int, default=200, help="Maximum matches to inspect.")
@click.option("--page-size", type=int, default=None, help="Number of matches to return.")
@click.option("--cursor", type=int, default=None, help="Zero-based result cursor.")
@click.option("--ignore-case/--case-sensitive", default=True, help="Case-insensitive search.")
@handle_unity_errors
def search(uri: str, pattern: str, max_results: int, page_size: Optional[int], cursor: Optional[int], ignore_case: bool):
    """Search a single file with a regex pattern."""
    config = get_config()
    params = {
        "action": "search",
        "uri": uri,
        "pattern": pattern,
        "max_results": max_results,
        "ignore_case": ignore_case,
    }
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    result = run_command("find_in_file", params, config)
    click.echo(format_output(result, config.format))


@find_in_file.command("references")
@click.argument("symbol_name")
@click.option("--scope", default=None, help="Asset scope to scan, default Assets.")
@click.option("--max-results", type=int, default=200, help="Maximum references to scan.")
@click.option("--page-size", type=int, default=None, help="Number of references to return.")
@click.option("--cursor", type=int, default=None, help="Zero-based result cursor.")
@handle_unity_errors
def references(symbol_name: str, scope: Optional[str], max_results: int, page_size: Optional[int], cursor: Optional[int]):
    """Find lexical references to a symbol across project scripts."""
    config = get_config()
    params = {
        "action": "find_references",
        "symbolName": symbol_name,
        "uri": "Assets",
        "pattern": symbol_name,
        "max_results": max_results,
    }
    if scope:
        params["scope"] = scope
    if page_size is not None:
        params["page_size"] = page_size
    if cursor is not None:
        params["cursor"] = cursor
    result = run_command("find_in_file", params, config)
    click.echo(format_output(result, config.format))
