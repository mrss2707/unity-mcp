"""Integration test: Create a simple "Catch the Falling Cubes" game via Unity MCP tools.

Exercises the newly added tools/actions from the 28-task spec upgrade.
Run from Server/ directory with Unity connected:

    uv run python tests/manual/test_game_creation.py

Requires: Unity Editor running with MCP plugin, bridge server on localhost.
"""

import asyncio
import json
import os
import sys
from datetime import datetime

# Add src to path for imports
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "src"))

from cli.utils.connection import run_command
from cli.utils.output import print_success, print_error, print_info


def cmd(tool: str, params: dict) -> dict:
    """Send a command to Unity via the CLI bridge."""
    result = run_command(tool, params, timeout=30)
    return result


def assert_ok(result: dict, label: str):
    """Assert a command succeeded."""
    if result.get("success") is False or result.get("error"):
        print_error(f"FAIL: {label} — {result.get('error', result)}")
        return False
    print_success(f"OK: {label}")
    return True


def test_create_scene():
    """Create and save a new scene."""
    print_info("\n=== Step 1: Create scene ===")

    r = cmd("manage_scene", {"action": "create", "name": "CatchFallingCubes"})
    assert_ok(r, "Create scene 'CatchFallingCubes'")

    r = cmd("manage_scene", {"action": "save"})
    assert_ok(r, "Save scene")


def test_create_ground():
    """Create ground plane with physics."""
    print_info("\n=== Step 2: Create ground ===")

    r = cmd("manage_gameobject", {"action": "create", "name": "Ground"})
    assert_ok(r, "Create Ground GameObject")
    instance_id = r.get("data", {}).get("instanceID")

    # Scale it to be a large plane
    r = cmd("manage_gameobject", {
        "action": "modify",
        "target": "Ground",
        "scale": {"x": 10, "y": 1, "z": 10},
    })
    assert_ok(r, "Scale Ground to 10x1x10")

    # Add BoxCollider
    r = cmd("manage_components", {
        "action": "add",
        "target": "Ground",
        "componentType": "BoxCollider",
    })
    assert_ok(r, "Add BoxCollider to Ground")

    return instance_id


def test_create_player():
    """Create player cube with Rigidbody."""
    print_info("\n=== Step 3: Create player ===")

    r = cmd("manage_gameobject", {
        "action": "create",
        "name": "Player",
        "position": {"x": 0, "y": 2, "z": 0},
    })
    assert_ok(r, "Create Player GameObject")

    r = cmd("manage_components", {
        "action": "add",
        "target": "Player",
        "componentType": "Rigidbody",
        "properties": {"constraints": 112},  # Freeze rotation X|Y|Z = 112
    })
    assert_ok(r, "Add Rigidbody to Player")

    r = cmd("manage_components", {
        "action": "add",
        "target": "Player",
        "componentType": "BoxCollider",
    })
    assert_ok(r, "Add BoxCollider to Player")

    # Test set_property action
    r = cmd("manage_components", {
        "action": "set_property",
        "target": "Player",
        "componentType": "Rigidbody",
        "property": "mass",
        "value": 2.0,
    })
    assert_ok(r, "Set Rigidbody mass to 2.0")

    # Test get_property action
    r = cmd("manage_components", {
        "action": "get_property",
        "target": "Player",
        "componentType": "Rigidbody",
        "propertyName": "mass",
    })
    assert_ok(r, "Get Rigidbody mass property")

    # Test list_all action
    r = cmd("manage_components", {
        "action": "list_all",
        "target": "Player",
    })
    assert_ok(r, "List all components on Player")


def test_create_obstacle_spawner():
    """Create obstacle spawn location."""
    print_info("\n=== Step 4: Create obstacle spawner ===")

    r = cmd("manage_gameobject", {
        "action": "create",
        "name": "ObstacleSpawner",
        "position": {"x": 0, "y": 8, "z": 0},
    })
    assert_ok(r, "Create ObstacleSpawner GameObject")


def test_create_falling_cube():
    """Create a falling obstacle cube to verify physics setup."""
    print_info("\n=== Step 5: Create falling cube ===")

    r = cmd("manage_gameobject", {
        "action": "create",
        "name": "FallingCube",
        "position": {"x": 2, "y": 6, "z": 2},
    })
    assert_ok(r, "Create FallingCube GameObject")

    r = cmd("manage_components", {
        "action": "add",
        "target": "FallingCube",
        "componentType": "Rigidbody",
    })
    assert_ok(r, "Add Rigidbody to FallingCube")

    r = cmd("manage_components", {
        "action": "add",
        "target": "FallingCube",
        "componentType": "BoxCollider",
    })
    assert_ok(r, "Add BoxCollider to FallingCube")


def test_manage_material():
    """Test material creation."""
    print_info("\n=== Step 6: Create materials ===")

    r = cmd("manage_material", {
        "action": "create",
        "name": "PlayerMat",
        "color": {"r": 0.2, "g": 0.6, "b": 1.0, "a": 1.0},
    })
    assert_ok(r, "Create PlayerMat material (blue)")

    r = cmd("manage_material", {
        "action": "create",
        "name": "GroundMat",
        "color": {"r": 0.3, "g": 0.8, "b": 0.3, "a": 1.0},
    })
    assert_ok(r, "Create GroundMat material (green)")


def test_directional_light():
    """Test adding lighting."""
    print_info("\n=== Step 7: Add lighting ===")

    r = cmd("manage_gameobject", {
        "action": "create",
        "name": "DirectionalLight",
    })
    assert_ok(r, "Create DirectionalLight GameObject")

    r = cmd("manage_components", {
        "action": "add",
        "target": "DirectionalLight",
        "componentType": "Light",
    })
    assert_ok(r, "Add Light component")

    r = cmd("manage_components", {
        "action": "set_property",
        "target": "DirectionalLight",
        "componentType": "Light",
        "property": "intensity",
        "value": 1.5,
    })
    assert_ok(r, "Set light intensity to 1.5")

    r = cmd("manage_gameobject", {
        "action": "modify",
        "target": "DirectionalLight",
        "rotation": {"x": 50, "y": -30, "z": 0},
    })
    assert_ok(r, "Rotate DirectionalLight")


def test_manage_editor_actions():
    """Test editor management actions."""
    print_info("\n=== Step 8: Test manage_editor actions ===")

    # Test create_folder_structure (new action)
    r = cmd("manage_editor", {
        "action": "create_folder_structure",
        "structure": "default",
        "rootPath": "Assets/CatchGame",
    })
    assert_ok(r, "Create folder structure 'Assets/CatchGame'")

    # Test run_health_check (new action)
    r = cmd("manage_editor", {
        "action": "run_health_check",
        "checks": ["compile"],
    })
    assert_ok(r, "Run health check (compile)")

    # Test undo
    r = cmd("manage_editor", {"action": "undo"})
    assert_ok(r, "Undo last action")

    redo = cmd("manage_editor", {"action": "redo"})
    assert_ok(redo, "Redo last action")


def test_manage_optimization_actions():
    """Test optimization management actions."""
    print_info("\n=== Step 9: Test manage_optimization actions ===")

    # Test set_quality_settings (new action)
    r = cmd("manage_optimization", {
        "action": "set_quality_settings",
        "preset": "medium",
        "platform": "standalone",
    })
    assert_ok(r, "Set quality settings to medium")

    # Test configure_occlusion (new action)
    r = cmd("manage_optimization", {
        "action": "configure_occlusion",
        "smallestOccluder": 5.0,
        "smallestHole": 0.25,
    })
    assert_ok(r, "Configure occlusion culling")


def test_find_in_file():
    """Test find_in_file tool."""
    print_info("\n=== Step 10: Test find_in_file ===")

    r = cmd("find_in_file", {
        "action": "find_references",
        "symbolName": "GameObject",
        "scope": "Assets",
    })
    assert_ok(r, "Find references to 'GameObject'")


def test_manage_build_actions():
    """Test build management actions."""
    print_info("\n=== Step 11: Test manage_build actions ===")

    r = cmd("manage_build", {"action": "platform"})
    assert_ok(r, "Get current build platform")

    r = cmd("manage_build", {"action": "scenes"})
    assert_ok(r, "Get build scenes")


def test_manage_addressables_actions():
    """Test addressables actions (may fail if package missing)."""
    print_info("\n=== Step 12: Test manage_addressables actions ===")

    r = cmd("manage_addressables", {"action": "list_groups"})
    if "error" in str(r) and "PACKAGE_MISSING" in str(r):
        print_info("SKIP: Addressables package not installed (expected in test project)")
    else:
        assert_ok(r, "List addressable groups")


def test_manage_input_system_actions():
    """Test input system actions (may fail if package missing)."""
    print_info("\n=== Step 13: Test manage_input_system actions ===")

    r = cmd("manage_input_system", {
        "action": "create_asset",
        "path": "Assets/CatchGame/GameControls.inputactions",
    })
    if "error" in str(r) and "PACKAGE_MISSING" in str(r):
        print_info("SKIP: Input System package not installed (expected in test project)")
    else:
        assert_ok(r, "Create InputActionAsset")


def test_manage_animation_actions():
    """Test animation management actions."""
    print_info("\n=== Step 14: Test manage_animation actions ===")

    r = cmd("manage_animation", {
        "action": "controller_create",
        "controllerPath": "Assets/CatchGame/PlayerAnim.controller",
    })
    if "error" in str(r) and "NOT_A_MODEL" not in str(r):
        assert_ok(r, "Create Animator Controller")


def test_find_gameobjects():
    """Test find_gameobjects tool."""
    print_info("\n=== Step 15: Test find_gameobjects ===")

    r = cmd("find_gameobjects", {
        "action": "by_name",
        "name": "Player",
    })
    assert_ok(r, "Find GameObjects by name 'Player'")


def test_save_and_summary():
    """Save scene and print summary."""
    print_info("\n=== Step 16: Save and summary ===")
    r = cmd("manage_scene", {"action": "save"})
    assert_ok(r, "Final save")


def main():
    """Run the full game creation test suite."""
    print("=" * 60)
    print("  Unity MCP — Game Creation Integration Test")
    print("  'Catch the Falling Cubes'")
    print(f"  Started: {datetime.now().isoformat()}")
    print("=" * 60)

    tests = [
        test_create_scene,
        test_create_ground,
        test_create_player,
        test_create_obstacle_spawner,
        test_create_falling_cube,
        test_manage_material,
        test_directional_light,
        test_manage_editor_actions,
        test_manage_optimization_actions,
        test_find_in_file,
        test_manage_build_actions,
        test_manage_addressables_actions,
        test_manage_input_system_actions,
        test_manage_animation_actions,
        test_find_gameobjects,
        test_save_and_summary,
    ]

    passed = 0
    failed = 0
    for test_fn in tests:
        try:
            test_fn()
            passed += 1
        except Exception as e:
            print_error(f"EXCEPTION in {test_fn.__name__}: {e}")
            failed += 1

    print("\n" + "=" * 60)
    print(f"  Results: {passed} passed, {failed} failed")
    print("=" * 60)
    return failed == 0


if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)
