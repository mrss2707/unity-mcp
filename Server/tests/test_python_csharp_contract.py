"""Cross-language contract check: every parameter a Python MCP tool sends must be read by the
matching C# handler, and every parameter the C# handler *requires* must be reachable from Python.

Layer 1 and Layer 3 of this project are written by hand and never generated from each other, so a
renamed parameter on one side produces a tool that fails at runtime while every unit test passes —
the Python tests mock the transport, and the C# side is never exercised by them. This test closes
that gap statically.

Python side is captured by invoking each tool with a stubbed transport, so it reflects what the tool
actually puts on the wire. C# side is parsed from the `ToolParams` accessors, whose first string
literal argument is always the parameter key.
"""
from __future__ import annotations

import asyncio
import inspect
import re
import typing
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

import services.tools as tools_pkg
from services.registry import get_registered_tools

CSHARP_TOOLS_DIR = Path(__file__).resolve().parents[2] / "MCPForUnity" / "Editor" / "Tools"
PAGINATION_HELPER_KEYS = {"page_size", "pageSize", "cursor", "page_number", "pageNumber"}

# Keys handled by the transport/registry layer rather than by a tool's own C# handler.
TRANSPORT_KEYS = {"action", "unity_instance", "client_id"}

# Contract breaks that already exist. The gate blocks *new* drift while these are worked off;
# delete each entry as it is fixed. Tracked in docs/development/develop-audit.md.
# Do not add to these lists to make a failure go away — a new entry means a new broken tool.
KNOWN_IGNORED = {
}
KNOWN_UNREACHABLE = {
}

# ToolParams accessors plus the raw JObject indexer. The first string literal is always the key
# (every accessor's second argument is a default value or an error message, never another key).
CSHARP_KEY_PATTERNS = [
    # Any ToolParams accessor: Get, GetRequired, GetInt, GetNullableBool, GetStringArray, Has, ...
    # Every one of them takes the parameter key as its first argument.
    re.compile(r'\b(?:\w+)\.(?:Get\w*|Has)\s*\(\s*"([^"]+)"'),
    # Direct JObject indexing, e.g. ManageScene's `p["sceneName"] ?? p["scene_name"]`.
    # No \b before @params: '@' is not a word character, so \b would never match there.
    re.compile(r'(?:\bp|@params|\bparameters)\s*\[\s*"([^"]+)"\s*\]'),
    # JObject.TryGetValue, used by tools that pull a nested blob such as `properties`.
    re.compile(r'\.TryGetValue\s*\(\s*"([^"]+)"'),
]
CSHARP_REQUIRED_PATTERN = re.compile(r'\b(?:\w+)\.GetRequired\s*\(\s*"([^"]+)"')
CSHARP_TOOL_ATTR = re.compile(r'\[McpForUnityTool\s*\(\s*"([^"]+)"')


def _dummy_for(annotation):
    """Build a value satisfying an annotation well enough for the tool to reach its send call."""
    origin = typing.get_origin(annotation)

    if origin is typing.Annotated:
        return _dummy_for(typing.get_args(annotation)[0])

    if origin is typing.Literal:
        return typing.get_args(annotation)[0]

    # Optional[X] / X | None / unions — take the first non-None member.
    if origin in (typing.Union, getattr(__import__("types"), "UnionType", None)):
        for arg in typing.get_args(annotation):
            if arg is not type(None):
                return _dummy_for(arg)
        return None

    if origin in (list, set, tuple):
        args = typing.get_args(annotation)
        return [_dummy_for(args[0])] if args else ["x"]

    if origin is dict:
        return {"k": "v"}

    if annotation is bool:
        return True
    if annotation is int:
        return 1
    if annotation is float:
        return 1.0
    if annotation is str:
        return "x"
    if annotation is dict:
        return {"k": "v"}
    if annotation is list:
        return ["x"]

    return "x"


def _actions_for(func) -> list:
    """Every value of the tool's `action` Literal, or [None] when it has no action parameter."""
    sig = inspect.signature(func)
    param = sig.parameters.get("action")
    if param is None or param.annotation is inspect.Parameter.empty:
        return [None]

    annotation = param.annotation
    while typing.get_origin(annotation) is typing.Annotated:
        annotation = typing.get_args(annotation)[0]

    candidates = [annotation]
    if typing.get_origin(annotation) in (typing.Union, getattr(__import__("types"), "UnionType", None)):
        candidates = list(typing.get_args(annotation))

    for candidate in candidates:
        if typing.get_origin(candidate) is typing.Literal:
            return list(typing.get_args(candidate))
    return [None]


def _capture_python_keys(monkeypatch, tool: dict) -> dict[str, set[str]]:
    """Invoke the tool once per action with a stubbed transport.

    Returns Unity-command-name -> keys sent. Keyed on the name actually passed to the transport,
    not the declared `unity_target`, because some tools dispatch to a different handler per action.
    """
    func = tool["func"]
    module = inspect.getmodule(func)
    sent: dict[str, set[str]] = {}

    async def fake_send(_send_fn, _instance, tool_name, params, *a, **kw):
        if isinstance(params, dict):
            sent.setdefault(tool_name, set()).update(params.keys())
        return {"success": True}

    class DummyTransport:
        async def send_command(self, tool_name, params):
            if isinstance(params, dict):
                sent.setdefault(tool_name, set()).update(params.keys())
            return {"success": True}

        async def send_command_with_retry(self, tool_name, params):
            if isinstance(params, dict):
                sent.setdefault(tool_name, set()).update(params.keys())
            return {"success": True}

    for attr, replacement in (
        ("send_with_unity_instance", fake_send),
        ("get_unity_instance_from_context", AsyncMock(return_value="unity-1")),
    ):
        if hasattr(module, attr):
            monkeypatch.setattr(f"{module.__name__}.{attr}", replacement, raising=False)

    # Tools whose `action` is a plain str rather than a Literal publish the valid values as a
    # module-level ALL_ACTIONS constant; without them the tool rejects every dummy action and
    # never reaches its send call.
    actions = _actions_for(func)
    if actions == [None]:
        declared = getattr(module, "ALL_ACTIONS", None)
        if declared:
            actions = list(declared)

    sig = inspect.signature(func)
    for action in actions:
        kwargs = {}
        for name, param in sig.parameters.items():
            if name == "ctx":
                continue
            if name == "action":
                kwargs[name] = action
                continue
            if param.annotation is inspect.Parameter.empty:
                continue
            kwargs[name] = _dummy_for(param.annotation)
        try:
            asyncio.run(func(SimpleNamespace(), **kwargs))
        except Exception:
            # A tool that rejects the dummy payload for one action simply contributes no keys
            # for it; other actions still cover the parameter surface.
            continue

    return {target: keys - TRANSPORT_KEYS for target, keys in sent.items()}


def _parse_csharp() -> tuple[dict[str, set[str]], dict[str, set[str]]]:
    """Map Unity tool name -> (all keys read, keys read via GetRequired)."""
    read: dict[str, set[str]] = {}
    required: dict[str, set[str]] = {}

    for path in CSHARP_TOOLS_DIR.rglob("*.cs"):
        source = path.read_text(encoding="utf-8", errors="replace")
        match = CSHARP_TOOL_ATTR.search(source)
        if not match:
            continue
        name = match.group(1)
        keys = read.setdefault(name, set())
        for pattern in CSHARP_KEY_PATTERNS:
            keys.update(pattern.findall(source))
        if "PaginationRequest.FromParams" in source:
            keys.update(PAGINATION_HELPER_KEYS)
        required.setdefault(name, set()).update(CSHARP_REQUIRED_PATTERN.findall(source))

    # Handlers are routinely split across helper files that carry no attribute of their own —
    # siblings (Cameras/CameraCreate.cs) and nested ones (Profiler/Operations/MemorySnapshotOps.cs).
    # Fold each such file into the tool(s) declared in its nearest attributed ancestor directory.
    # The Tools/ root is excluded: it holds many unrelated tools, so folding there matches everything.
    tools_by_dir: dict[Path, set[str]] = {}
    for path in CSHARP_TOOLS_DIR.rglob("*.cs"):
        match = CSHARP_TOOL_ATTR.search(path.read_text(encoding="utf-8", errors="replace"))
        if match:
            tools_by_dir.setdefault(path.parent, set()).add(match.group(1))

    for path in CSHARP_TOOLS_DIR.rglob("*.cs"):
        source = path.read_text(encoding="utf-8", errors="replace")
        if CSHARP_TOOL_ATTR.search(source):
            continue
        for ancestor in [path.parent, *path.parent.parents]:
            if ancestor == CSHARP_TOOLS_DIR or CSHARP_TOOLS_DIR not in ancestor.parents:
                break
            if ancestor in tools_by_dir:
                for name in tools_by_dir[ancestor]:
                    for pattern in CSHARP_KEY_PATTERNS:
                        read[name].update(pattern.findall(source))
                    if "PaginationRequest.FromParams" in source:
                        read[name].update(PAGINATION_HELPER_KEYS)
                break

    return read, required


@pytest.fixture(scope="module")
def csharp_keys():
    return _parse_csharp()


@pytest.fixture(scope="module", autouse=True)
def _load_tools():
    # The @mcp_for_unity_tool decorator populates the registry at import time.
    import importlib
    import pkgutil

    for module in pkgutil.iter_modules(tools_pkg.__path__):
        importlib.import_module(f"{tools_pkg.__name__}.{module.name}")


def _canonical(key: str) -> str:
    """snake_case and camelCase spellings of the same parameter collapse to one form."""
    return key.replace("_", "").lower()


@pytest.fixture(scope="module")
def python_keys(request):
    """Unity-command-name -> every key any Python tool sends to it."""
    monkeypatch = pytest.MonkeyPatch()
    request.addfinalizer(monkeypatch.undo)

    merged: dict[str, set[str]] = {}
    for tool in get_registered_tools():
        for target, keys in _capture_python_keys(monkeypatch, tool).items():
            merged.setdefault(target, set()).update(keys)
    return merged


def test_allowlists_have_no_stale_entries(python_keys, csharp_keys):
    """Every quarantined entry must still describe a real break.

    Without this, fixing the C# side and forgetting to delete the allowlist entry leaves the gate
    green *and* permanently blind to that key — the quarantine silently becomes a blindfold. Mirrors
    test_tool_test_symmetry.py's stale-quarantine guard. These lists may only shrink.
    """
    read, required = csharp_keys
    stale: list[str] = []

    for target, keys in KNOWN_IGNORED.items():
        readable = {_canonical(k) for k in read.get(target, set())}
        sent = python_keys.get(target, set())
        for key in keys:
            if key not in sent:
                stale.append(f"KNOWN_IGNORED[{target}][{key}]: Python no longer sends it")
            elif _canonical(key) in readable:
                stale.append(f"KNOWN_IGNORED[{target}][{key}]: C# now reads it — fixed, delete the entry")

    for target, keys in KNOWN_UNREACHABLE.items():
        available = {_canonical(k) for k in python_keys.get(target, set())}
        for key in keys:
            if key not in required.get(target, set()):
                stale.append(f"KNOWN_UNREACHABLE[{target}][{key}]: C# no longer requires it")
            elif _canonical(key) in available:
                stale.append(f"KNOWN_UNREACHABLE[{target}][{key}]: Python now sends it — fixed, delete the entry")

    assert not stale, "Stale quarantine entries — delete them:\n" + "\n".join(f"  {s}" for s in stale)


def test_no_python_parameter_is_ignored_by_unity(python_keys, csharp_keys):
    """A parameter Python sends but C# never reads is dead: the agent sets it and nothing happens."""
    read, _ = csharp_keys
    offenders: dict[str, set[str]] = {}

    for target, sent in python_keys.items():
        if target not in read:
            continue  # handled entirely server-side, no C# counterpart in this repo
        readable = {_canonical(k) for k in read[target]}
        ignored = {k for k in sent if _canonical(k) not in readable}
        ignored -= KNOWN_IGNORED.get(target, set())
        if ignored:
            offenders[target] = ignored

    assert not offenders, "Parameters sent by Python that no C# handler reads:\n" + "\n".join(
        f"  {target}: {', '.join(sorted(keys))}" for target, keys in sorted(offenders.items())
    )


def test_every_required_unity_parameter_is_reachable_from_python(python_keys, csharp_keys):
    """A key C# demands via GetRequired but Python never sends makes that action always fail."""
    _, required = csharp_keys
    offenders: dict[str, set[str]] = {}

    for target, needed in required.items():
        if target not in python_keys:
            continue
        available = {_canonical(k) for k in python_keys[target]}
        unreachable = {
            k for k in needed
            if k not in TRANSPORT_KEYS and _canonical(k) not in available
        }
        unreachable -= KNOWN_UNREACHABLE.get(target, set())
        if unreachable:
            offenders[target] = unreachable

    assert not offenders, "Parameters C# requires that Python never sends:\n" + "\n".join(
        f"  {target}: {', '.join(sorted(keys))}" for target, keys in sorted(offenders.items())
    )
