import base64
import os
import re
from typing import Annotated, Any, Literal
from urllib.parse import unquote, urlparse

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight
from services.tools.utils import coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _split_uri(uri: str) -> tuple[str, str]:
    """Split an incoming URI or path into (name, directory) suitable for Unity.

    Rules:
    - mcpforunity://path/Assets/... → keep as Assets-relative (after decode/normalize)
    - file://... → percent-decode, normalize, strip host and leading slashes,
        then, if any 'Assets' segment exists, return path relative to that 'Assets' root.
        Otherwise, fall back to original name/dir behavior.
    - plain paths → decode/normalize separators; if they contain an 'Assets' segment,
        return relative to 'Assets'.
    """
    raw_path: str
    if uri.startswith("mcpforunity://path/"):
        raw_path = uri[len("mcpforunity://path/"):]
    elif uri.startswith("file://"):
        parsed = urlparse(uri)
        host = (parsed.netloc or "").strip()
        p = parsed.path or ""
        if host and host.lower() != "localhost":
            p = f"//{host}{p}"
        raw_path = unquote(p)
    else:
        raw_path = uri

    raw_path = unquote(raw_path).replace("\\", "/")
    if os.name == "nt" and len(raw_path) >= 3 and raw_path[0] == "/" and raw_path[2] == ":":
        raw_path = raw_path[1:]

    norm = os.path.normpath(raw_path).replace("\\", "/")
    parts = [p for p in norm.split("/") if p not in ("", ".")]
    idx = next((i for i, seg in enumerate(parts) if seg.lower() == "assets"), None)
    assets_rel = "/".join(parts[idx:]) if idx is not None else None

    effective_path = assets_rel if assets_rel else norm
    if effective_path.startswith("/"):
        effective_path = effective_path[1:]

    name = os.path.splitext(os.path.basename(effective_path))[0]
    directory = os.path.dirname(effective_path)
    return name, directory


def _pagination(page_size: int | str | None, cursor: int | str | None, max_results: int) -> tuple[int, int]:
    page_size_i = coerce_int(page_size, default=min(50, max_results)) or min(50, max_results)
    cursor_i = coerce_int(cursor, default=0) or 0
    return max(1, min(page_size_i, max_results)), max(0, cursor_i)


@mcp_for_unity_tool(
    unity_target="manage_script",
    description="Searches a file with a regex pattern and returns line numbers and excerpts.",
    annotations=ToolAnnotations(
        title="Find in File",
        readOnlyHint=True,
        destructiveHint=False,
        idempotentHint=True,
        openWorldHint=False,
    ),
)
async def find_in_file(
    ctx: Context,
    uri: Annotated[str, "The resource URI to search under Assets/ or file path form supported by read_resource"],
    pattern: Annotated[str, "The regex pattern to search for"],
    action: Annotated[Literal["search", "find_references"] | None, "Action: search (regex search in file) or find_references (find symbol references across project)."] = None,
    symbolName: Annotated[str | None, "Symbol/class name to find references for (find_references action)."] = None,
    scope: Annotated[str | None, "Search scope path (default: Assets)."] = None,
    project_root: Annotated[str | None, "Optional project root path"] = None,
    max_results: Annotated[int, "Cap results to avoid huge payloads"] = 200,
    ignore_case: Annotated[bool | str | None, "Case insensitive search"] = True,
    page_size: Annotated[int | str | None, "Number of results to return per page."] = None,
    cursor: Annotated[int | str | None, "Zero-based result cursor for paging."] = None,
) -> dict[str, Any]:
    # project_root is currently unused but kept for interface consistency
    unity_instance = await get_unity_instance_from_context(ctx)
    max_results_i = max(1, coerce_int(max_results, default=200) or 200)
    page_size_i, cursor_i = _pagination(page_size, cursor, max_results_i)

    if action == "find_references":
        if not symbolName:
            return {"success": False, "message": "symbolName is required for find_references action."}

        gate = await preflight(ctx, wait_for_no_compile=True)
        if gate is not None:
            return gate.model_dump()

        refs_params: dict[str, Any] = {
            "action": "find_references",
            "symbolName": symbolName,
            "maxResults": max_results_i,
            "pageSize": page_size_i,
            "cursor": cursor_i,
        }
        if scope is not None:
            refs_params["scope"] = scope
        result = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "find_in_file",
            refs_params,
        )
        return result if isinstance(result, dict) else {"success": False, "message": str(result)}

    flags = re.MULTILINE
    ic = ignore_case
    if isinstance(ic, str):
        ic = ic.lower() in ("true", "1", "yes")
    if ic:
        flags |= re.IGNORECASE

    try:
        regex = re.compile(pattern, flags)
    except re.error as e:
        return {"success": False, "message": f"Invalid regex pattern: {e}"}

    gate = await preflight(ctx, wait_for_no_compile=True)
    if gate is not None:
        return gate.model_dump()

    await ctx.info(f"Processing find_in_file: {uri} (unity_instance={unity_instance or 'default'})")

    name, directory = _split_uri(uri)

    read_resp = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_script",
        {
            "action": "read",
            "name": name,
            "path": directory,
        },
    )

    if not isinstance(read_resp, dict) or not read_resp.get("success"):
        return read_resp if isinstance(read_resp, dict) else {"success": False, "message": str(read_resp)}

    data = read_resp.get("data", {})
    contents = data.get("contents")
    if not contents and data.get("contentsEncoded") and data.get("encodedContents"):
        try:
            contents = base64.b64decode(data.get("encodedContents", "").encode("utf-8")).decode("utf-8", "replace")
        except (ValueError, TypeError, base64.binascii.Error):
            contents = contents or ""

    if contents is None:
        return {"success": False, "message": "Could not read file content."}

    results = []
    total_seen = 0
    truncated = False

    for m in regex.finditer(contents):
        total_seen += 1
        if total_seen > max_results_i:
            truncated = True
            break

        if total_seen <= cursor_i or len(results) >= page_size_i:
            continue

        start_idx = m.start()
        line_num = contents.count('\n', 0, start_idx) + 1
        line_start = contents.rfind('\n', 0, start_idx) + 1
        line_end = contents.find('\n', start_idx)
        if line_end == -1:
            line_end = len(contents)

        results.append({
            "line": line_num,
            "content": contents[line_start:line_end].strip(),
            "match": m.group(0),
            "start": start_idx,
            "end": m.end(),
        })

    total_matches = max_results_i if truncated else total_seen
    has_more = truncated or total_matches > cursor_i + len(results)
    next_cursor = cursor_i + len(results) if has_more else None

    return {
        "success": True,
        "data": {
            "matches": results,
            "count": len(results),
            "total_matches": total_matches,
            "maxResults": max_results_i,
            "truncatedByMaxResults": truncated,
            "pageSize": page_size_i,
            "cursor": cursor_i,
            "nextCursor": next_cursor,
            "hasMore": has_more,
        },
    }
