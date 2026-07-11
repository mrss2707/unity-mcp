using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Profiler
{
    [McpForUnityTool("manage_profiler", AutoRegister = false, Group = "profiling")]
    public static class ManageProfiler
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                switch (action)
                {
                    // Session
                    case "profiler_start":
                        return SessionOps.Start(@params);
                    case "profiler_stop":
                        return SessionOps.Stop(@params);
                    case "profiler_status":
                        return SessionOps.Status(@params);
                    case "profiler_set_areas":
                        return SessionOps.SetAreas(@params);

                    // Counters
                    case "get_frame_timing":
                        return FrameTimingOps.GetFrameTiming(@params);
                    case "get_counters":
                        return await CounterOps.GetCountersAsync(@params);
                    case "get_object_memory":
                        return ObjectMemoryOps.GetObjectMemory(@params);

                    // Memory Snapshot
                    case "memory_take_snapshot":
                        return await MemorySnapshotOps.TakeSnapshotAsync(@params);
                    case "memory_list_snapshots":
                        return MemorySnapshotOps.ListSnapshots(@params);
                    case "memory_compare_snapshots":
                        return MemorySnapshotOps.CompareSnapshots(@params);

                    // Frame Debugger
                    case "frame_debugger_enable":
                        return FrameDebuggerOps.Enable(@params);
                    case "frame_debugger_disable":
                        return FrameDebuggerOps.Disable(@params);
                    case "frame_debugger_get_events":
                        return FrameDebuggerOps.GetEvents(@params);

                    // Utility
                    case "ping":
                        return new SuccessResponse("manage_profiler is available.", new
                        {
                            tool = "manage_profiler",
                            group = "profiling"
                        });

                    // Profiler Recorder (Unity 2020.2+)
                    case "get_draw_calls":
                    {
                        int frameIndex = p.GetInt("frameIndex", -1) ?? -1;

                        var recorder = ProfilerRecorder.StartNew(
                            ProfilerCategory.Render, "Draw Calls Count");

                        // Ensure one-frame delay for valid data
                        bool frameWaited = false;
                        EditorApplication.update += WaitOneFrame;

                        void WaitOneFrame()
                        {
                            EditorApplication.update -= WaitOneFrame;
                            frameWaited = true;
                        }

                        // Wait up to 2 seconds for the frame
                        var startTime = EditorApplication.timeSinceStartup;
                        while (!frameWaited && EditorApplication.timeSinceStartup - startTime < 2.0)
                        {
                            // Allow Unity to process
                        }

                        return new SuccessResponse("Draw call analysis", new
                        {
                            drawCalls = (int)recorder.LastValue,
                            batchedDrawCalls = (int)ProfilerRecorder
                                .StartNew(ProfilerCategory.Render, "Batched Draw Calls Count")
                                .LastValue,
                            setPassCalls = (int)ProfilerRecorder
                                .StartNew(ProfilerCategory.Render, "SetPass Calls Count")
                                .LastValue,
                            triangles = (int)ProfilerRecorder
                                .StartNew(ProfilerCategory.Render, "Triangles Count")
                                .LastValue,
                        });
                    }

                    case "get_gpu_profile":
                    {
                        var categories = new Dictionary<string, ProfilerRecorder>
                        {
                            ["opaque"] = ProfilerRecorder.StartNew(
                                ProfilerCategory.Render, "Opaque Geometry"),
                            ["transparent"] = ProfilerRecorder.StartNew(
                                ProfilerCategory.Render, "Transparent Geometry"),
                            ["shadows"] = ProfilerRecorder.StartNew(
                                ProfilerCategory.Render, "Shadows"),
                            ["postProcessing"] = ProfilerRecorder.StartNew(
                                ProfilerCategory.Render, "Post-Processing"),
                        };

                        var result = categories.ToDictionary(
                            kv => kv.Key,
                            kv => (long)kv.Value.LastValue);
                        return new SuccessResponse("GPU profile", result);
                    }

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: "
                            + "profiler_start, profiler_stop, profiler_status, profiler_set_areas, "
                            + "get_frame_timing, get_counters, get_object_memory, "
                            + "memory_take_snapshot, memory_list_snapshots, memory_compare_snapshots, "
                            + "frame_debugger_enable, frame_debugger_disable, frame_debugger_get_events, "
                            + "get_draw_calls, get_gpu_profile, ping.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageProfiler] Action '{action}' failed: {ex}");
                return new ErrorResponse($"Error in action '{action}': {ex.Message}");
            }
        }
    }
}
