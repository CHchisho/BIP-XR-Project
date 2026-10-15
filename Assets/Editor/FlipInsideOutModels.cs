using UnityEditor;
using UnityEngine;

namespace SmartHouse.EditorTools
{
    /// <summary>
    /// Some device FBX exports come out inside-out (inward winding and normals), so
    /// Unity culls the outer faces and the model looks hollow/see-through. This flips
    /// the triangle winding and normals on import for the listed models.
    /// </summary>
    class FlipInsideOutModels : AssetPostprocessor
    {
        static readonly string[] Paths =
        {
            "Assets/Models/Devices/AqaraThermostat.fbx",
        };

        void OnPostprocessModel(GameObject root)
        {
            if (System.Array.IndexOf(Paths, assetPath) < 0) return;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = mesh.GetTriangles(s);
                    for (int i = 0; i < tris.Length; i += 3)
                        (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                    mesh.SetTriangles(tris, s);
                }

                var normals = mesh.normals;
                for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
                mesh.normals = normals;
                mesh.RecalculateTangents();
            }
        }
    }
}
