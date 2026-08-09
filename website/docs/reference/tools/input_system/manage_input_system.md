---
title: manage_input_system
sidebar_label: manage_input_system
description: "Manage Unity Input System Action Assets — create, read, and modify InputActionAssets with action maps, bindings, control schemes, and composite bindings."
---

# `manage_input_system`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `input_system` &nbsp;·&nbsp; **Module:** `services.tools.manage_input_system`

## Description

Manage Unity Input System Action Assets — create, read, and modify InputActionAssets with action maps, bindings, control schemes, and composite bindings.

ASSET OPERATIONS:
- create_asset: Create a new InputActionAsset at the specified path
- get_asset: Retrieve details of an existing InputActionAsset

ACTION MAP OPERATIONS:
- add_action_map: Add a new action map to an asset
- remove_action_map: Remove an action map from an asset

ACTION OPERATIONS:
- add_action: Add a new action to an action map
- remove_action: Remove an action from an action map
- rename_action: Rename an existing action

CONTROL SCHEME OPERATIONS:
- add_control_scheme: Add a new control scheme to an asset
- remove_control_scheme: Remove a control scheme from an asset

BINDING OPERATIONS:
- add_bindings: Add binding(s) to an action
- remove_bindings: Remove binding(s) from an action
- add_composite: Add a composite binding (1DAxis, 2DVector, etc.) to an action

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create_asset', 'get_asset', 'add_action_map', 'remove_action_map', 'add_action', 'remove_action', 'rename_action', 'add_control_scheme', 'remove_control_scheme', 'add_bindings', 'remove_bindings', 'add_composite']` | yes | The operation to perform on the Input System asset. |
| `assetPath` | `str \| None` | — | Path to the InputActionAsset (.inputactions). |
| `mapName` | `str \| None` | — | Action map name. |
| `actionName` | `str \| None` | — | Action name within an action map. |
| `actionType` | `Literal['Button', 'Value', 'PassThrough'] \| None` | — | Input action type. |
| `controlLayout` | `Literal['Button', 'Vector2', 'Vector3', 'Axis', 'Key', 'Stick', 'Dpad', 'Touch'] \| None` | — | Expected control layout. |
| `binding` | `str \| None` | — | Single binding path (e.g. <Keyboard>/space). |
| `bindings` | `list[str] \| None` | — | List of binding paths. |
| `interactions` | `str \| None` | — | Interactions string (e.g. Hold, Press). |
| `processors` | `str \| None` | — | Processors string (e.g. Normalize). |
| `groups` | `str \| None` | — | Control scheme groups (comma-separated). |
| `schemeName` | `str \| None` | — | Control scheme name. |
| `requiredDevices` | `list[str] \| None` | — | Required devices for control scheme. |
| `optionalDevices` | `list[str] \| None` | — | Optional devices for control scheme. |
| `oldName` | `str \| None` | — | Current action name (for rename). |
| `newName` | `str \| None` | — | New action name (for rename). |
| `compositeType` | `Literal['1DAxis', '2DVector', '3DVector', 'Dpad', 'Stick'] \| None` | — | Composite binding type. |
| `compositeName` | `str \| None` | — | Name for the composite binding. Defaults to compositeType. |
| `parts` | `dict[str, str] \| None` | — | Composite parts as part-name to binding path, e.g. {"up": "<Keyboard>/w", "down": "<Keyboard>/s"}. Required for add_composite. |
| `page_size` | `int \| str \| None` | — | Number of action maps to return for get_asset. |
| `cursor` | `int \| str \| None` | — | Zero-based action map cursor for get_asset. |
| `include_json` | `bool \| str \| None` | — | Include a bounded JSON chunk for get_asset. |
| `json_cursor` | `int \| str \| None` | — | Zero-based JSON character cursor for get_asset. |
| `json_chunk_size` | `int \| str \| None` | — | Maximum JSON characters to return per get_asset chunk. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

