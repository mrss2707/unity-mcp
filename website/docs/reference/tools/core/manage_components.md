---
title: manage_components
sidebar_label: manage_components
description: "Add, remove, or set properties on components attached to GameObjects."
---

# `manage_components`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_components`

## Description

Add, remove, or set properties on components attached to GameObjects. Actions: add, remove, set_property, get_property, list_all, add_simple_listener, add_param_listener, remove_listener, get_listeners. Requires target (instance ID or name) and component_type for add/remove/set_property. Use gameObjectPath for inspection/listener actions. For READING component data, use the mcpforunity://scene/gameobject/{id}/components resource or mcpforunity://scene/gameobject/{id}/component/{name} for a single component. For creating/deleting GameObjects themselves, use manage_gameobject instead.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['add', 'remove', 'set_property', 'get_property', 'list_all', 'add_simple_listener', 'add_param_listener', 'remove_listener', 'get_listeners']` | yes | Action to perform: add, remove, set_property, get_property, list_all, add_simple_listener, add_param_listener, remove_listener, get_listeners |
| `target` | `str \| int \| None` | — | Target GameObject - instance ID (preferred) or name/path. Required for add/remove/set_property. |
| `component_type` | `str \| None` | — | Component type name (e.g., 'Rigidbody', 'BoxCollider', 'MyScript'). Required for add/remove/set_property/get_property. |
| `search_method` | `Literal['by_id', 'by_name', 'by_path'] \| None` | — | How to find the target GameObject |
| `property` | `str \| None` | — | Property name to set (for set_property action) |
| `value` | `str \| int \| float \| bool \| dict[Any] \| list[Any] \| None` | — | Value to set (for set_property action). For object references: instance ID (int), asset path (string), or {"guid": "..."} / {"path": "..."}. For Sprite sub-assets: {"guid": "...", "spriteName": "<name>"} or {"guid": "...", "fileID": <id>}. Single-sprite textures auto-resolve. |
| `properties` | `dict[str, Any] \| str \| None` | — | Dictionary of property names to values. Example: {"mass": 5.0, "useGravity": false} |
| `component_index` | `int \| None` | — | Zero-based index to select which component when multiple of the same type exist. Use the components resource to discover indices. If omitted, targets the first instance. |
| `gameObjectPath` | `str \| None` | — | Path to the target GameObject (for inspection/listener actions). |
| `propertyName` | `str \| None` | — | Name of the property to get (for get_property action). |
| `eventName` | `str \| None` | — | Name of the UnityEvent field (for listener actions). |
| `targetPath` | `str \| None` | — | Path to the target GameObject for the listener callback. |
| `methodName` | `str \| None` | — | Method name for the listener callback. |
| `paramType` | `Literal['int', 'float', 'string', 'bool', 'Object'] \| None` | — | Parameter type for typed listener. |
| `paramValue` | `str \| None` | — | Parameter value for typed listener (as string, will be parsed by C#). |
| `paramObjectPath` | `str \| None` | — | Path to the GameObject passed as the argument when paramType is 'Object'. |
| `listenerIndex` | `int \| None` | — | Index of the persistent listener to remove. |
| `page_size` | `int \| str \| None` | — | Number of items per page for list_all/get_listeners. |
| `cursor` | `int \| str \| None` | — | Zero-based cursor for list_all/get_listeners paging. |
| `include_inactive` | `bool \| str \| None` | — | Include inactive GameObjects for inspection/listener target lookup. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

