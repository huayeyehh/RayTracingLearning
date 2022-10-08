using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RayTracingObjectRoot))]
public class RayTracingObjectRootEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        RayTracingObjectRoot myScript = (RayTracingObjectRoot)target;

        // BuildBVH function
        if (GUILayout.Button("Test Build BVH")) { myScript.TestBuildBVH(); }

        // Test function
        if (GUILayout.Button("Test")) { myScript.Test(); }

        // Test Ray
        if (GUILayout.Button("Test Ray")) { myScript.TestRay(); }

        // Clear data function
        if (GUILayout.Button("Clear Data")) { myScript.ClearData(); }
    }
}