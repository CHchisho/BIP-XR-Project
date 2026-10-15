using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace SmartHouse.EditorTools
{
    /// <summary>
    /// Builds the neighbourhood around the house so the view out of the windows looks real:
    /// a lawn that runs to the horizon, a street with pavements and lamps, a garden fence, neighbouring
    /// houses, garden planting, scanned trees (Poly Haven, CC0) and a distant tree line.
    /// Trees are thinned scans with a crossed-quad impostor LOD (see Assets/Models/Exterior/README.md).
    /// Everything lives under one "Exterior" root that is rebuilt from scratch on every run.
    /// </summary>
    public static class SmartHouseExterior
    {
        const string ModelRoot = "Assets/Models/Exterior";
        const string GeneratedFolder = ModelRoot + "/Generated";
        const string TextureRoot = "Assets/Textures/Exterior";
        const string HouseMaterials = "Assets/Materials/House";
        const float GroundY = -0.205f;   // top of the house model's lawn slab

        static readonly string[] Trees = { "island_tree_02", "tree_small_02", "jacaranda_tree" };

        [MenuItem("Smart House/Exterior/1. Configure Imports + Materials")]
        public static void ConfigureImports()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelRoot, TextureRoot, "Assets/Models/Controllers" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    bool normal = n.EndsWith("_normal") || n.Contains("nor_gl");
                    bool impostor = n.Contains("impostor");
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = !normal && !n.Contains("_arm");
                    ti.alphaIsTransparency = !normal && path.EndsWith(".png");
                    ti.mipmapEnabled = true;
                    ti.maxTextureSize = impostor ? 2048 : 1024;
                    ti.anisoLevel = n.Contains("_diff") || n.Contains("nor_gl") ? 4 : 1;   // ground textures seen at grazing angles
                    ti.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var dir in Directory.GetDirectories(ModelRoot).Concat("Assets/Models/Controllers"))
            {
                foreach (var albedoPath in Directory.GetFiles(dir, "*_albedo.png"))
                {
                    string stem = Path.GetFileNameWithoutExtension(albedoPath);
                    stem = stem.Substring(0, stem.Length - "_albedo".Length);
                    var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Unix(albedoPath));
                    var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Unix(Path.Combine(dir, stem + "_normal.png")));
                    bool cutout = HasTransparency(albedoPath);
                    var mat = GetOrCreateMaterial($"{Unix(dir)}/M_{stem}.mat", lit);
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetColor("_BaseColor", Color.white);
                    SetNormal(mat, normal, 1f);
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Smoothness", stem.Contains("Controller") ? 0.45f : cutout ? 0.12f : 0.22f);
                    SetCutout(mat, cutout);
                    if (stem.Contains("impostor")) mat.SetFloat("_Cutoff", 0.45f);
                    mat.enableInstancing = true;
                    EditorUtility.SetDirty(mat);
                }

                foreach (var fbx in Directory.GetFiles(dir, "*.fbx"))
                {
                    var importer = (ModelImporter)AssetImporter.GetAtPath(Unix(fbx));
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.importAnimation = false;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.generateSecondaryUV = false;
                    importer.isReadable = false;
                    importer.meshCompression = ModelImporterMeshCompression.Medium;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    foreach (var matPath in Directory.GetFiles(dir, "M_*.mat"))
                    {
                        var m = AssetDatabase.LoadAssetAtPath<Material>(Unix(matPath));
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name.Substring(2)), m);
                    }
                    importer.SaveAndReimport();
                }
            }

            BuildHardscapeMaterials();
            BuildTreePrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log("[SmartHouse] Exterior imports and materials configured.");
        }

        // ---------------------------------------------------------------- materials

        static Material GetOrCreateMaterial(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static void SetNormal(Material mat, Texture2D normal, float scale)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", scale);
            if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
        }

        static void SetCutout(Material mat, bool cutout)
        {
            mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_Cull", cutout ? 0f : 2f);   // foliage cards are seen from both sides
            if (cutout)
            {
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = -1;
            }
        }

        /// <summary>True when the PNG has pixels that are clearly see-through (leaves, impostors).</summary>
        static bool HasTransparency(string path)
        {
            var tex = new Texture2D(2, 2);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) return false;
                var px = tex.GetPixels32();
                int clear = 0;
                for (int i = 0; i < px.Length; i += 7) if (px[i].a < 128) clear++;
                return clear > px.Length / 7 / 100;   // more than 1 % of samples
            }
            finally { Object.DestroyImmediate(tex); }
        }

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedFolder}/{name}.mat")
                                            ?? AssetDatabase.LoadAssetAtPath<Material>($"{HouseMaterials}/{name}.mat");

        static void BuildHardscapeMaterials()
        {
            Directory.CreateDirectory(GeneratedFolder);
            var lit = Shader.Find("Universal Render Pipeline/Lit");

            Material Textured(string name, string tex, float metresPerTile, float smoothness, Color tint)
            {
                var mat = GetOrCreateMaterial($"{GeneratedFolder}/{name}.mat", lit);
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureRoot}/{tex}/{tex}_diff_1k.jpg"));
                mat.SetTextureScale("_BaseMap", Vector2.one / metresPerTile);
                mat.SetColor("_BaseColor", tint);
                SetNormal(mat, AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureRoot}/{tex}/{tex}_nor_gl_1k.jpg"), 1f);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                return mat;
            }

            Material Plain(string name, Color color, float smoothness, float metallic = 0f, Color? emission = null)
            {
                var mat = GetOrCreateMaterial($"{GeneratedFolder}/{name}.mat", lit);
                mat.SetTexture("_BaseMap", null);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (emission.HasValue)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                return mat;
            }

            Textured("M_Ext_Lawn", "grass004", 1.5f, 0.08f, Color.white);
            Textured("M_Ext_Meadow", "grass001", 2.5f, 0.05f, new Color(0.95f, 0.97f, 0.9f));
            Textured("M_Ext_Asphalt", "asphalt_02", 4f, 0.18f, new Color(0.78f, 0.78f, 0.8f));
            Textured("M_Ext_Pavers", "concrete_pavers_02", 1.6f, 0.2f, new Color(0.9f, 0.9f, 0.92f));
            Textured("M_Ext_Mulch", "asphalt_02", 1.5f, 0.04f, new Color(0.42f, 0.3f, 0.22f));
            foreach (var tree in Trees)   // impostors are rendered under flat white light; keep them from glowing in the sun
            {
                var imp = AssetDatabase.LoadAssetAtPath<Material>($"{ModelRoot}/{tree}/M_{tree}_impostor.mat");
                if (imp != null) { imp.SetColor("_BaseColor", new Color(0.78f, 0.82f, 0.76f)); EditorUtility.SetDirty(imp); }
            }
            Plain("M_Ext_RoadPaint", new Color(0.86f, 0.86f, 0.82f), 0.25f);
            Plain("M_Ext_WindowDark", new Color(0.05f, 0.07f, 0.09f), 0.93f, 0.2f);
            Plain("M_Ext_Render_Warm", new Color(0.86f, 0.84f, 0.8f), 0.12f);
            Plain("M_Ext_Brick_Red", new Color(0.48f, 0.25f, 0.19f), 0.1f);
            Plain("M_Ext_LampGlow", new Color(1f, 0.97f, 0.9f), 0.5f, 0f, new Color(0.4f, 0.37f, 0.3f));
        }

        static void BuildTreePrefabs()
        {
            foreach (var tree in Trees)
            {
                string dir = $"{ModelRoot}/{tree}";
                var lod0 = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{tree}.fbx");
                var lod1 = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{tree}_Impostor.fbx");
                var impMat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/M_{tree}_impostor.mat");
                if (lod0 == null || lod1 == null || impMat == null) { Debug.LogWarning($"[SmartHouse] {tree}: assets missing, skipped."); continue; }

                var root = new GameObject($"Tree_{tree}");
                var a = (GameObject)PrefabUtility.InstantiatePrefab(lod0); a.transform.SetParent(root.transform, false); a.name = "LOD0";
                var b = (GameObject)PrefabUtility.InstantiatePrefab(lod1); b.transform.SetParent(root.transform, false); b.name = "LOD1 Impostor";
                foreach (var r in b.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = impMat;
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.22f, a.GetComponentsInChildren<Renderer>()),
                    new LOD(0.004f, b.GetComponentsInChildren<Renderer>()),
                });
                group.RecalculateBounds();
                foreach (var r in root.GetComponentsInChildren<Renderer>()) r.lightProbeUsage = LightProbeUsage.Off;
                PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/Tree_{tree}.prefab");
                Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- scene

        [MenuItem("Smart House/Exterior/2. Build Exterior")]
        public static void BuildExterior()
        {
            var old = GameObject.Find("Exterior");
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject("Exterior");
            Undo.RegisterCreatedObjectUndo(root, "Build Exterior");
            Directory.CreateDirectory(GeneratedFolder);
            var rnd = new Random(42);

            FixHouseModel();
            BuildGround(Group(root, "Ground"));
            BuildStreet(Group(root, "Street"));
            BuildFence(Group(root, "Garden Fence"));
            BuildNeighbours(Group(root, "Neighbour Houses"));
            BuildGarden(Group(root, "Garden"), rnd);
            BuildTrees(Group(root, "Trees"), rnd);
            BuildTreeLine(Group(root, "Distant Tree Line"), rnd);

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.lightProbeUsage = LightProbeUsage.Off;   // the probes are indoors; use the sky's ambient light
                if (r.GetComponentInParent<LODGroup>() == null)
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
            }

            // Gentle aerial haze so the distant tree line sits in the air instead of ending sharply.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0028f;
            RenderSettings.fogColor = new Color(0.74f, 0.79f, 0.84f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[SmartHouse] Exterior built.");
        }

        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            return g.transform;
        }

        /// <summary>Swap the flat procedural lawn for the scanned grass texture and drop the two placeholder lollipop trees.</summary>
        static void FixHouseModel()
        {
            var ground = GameObject.Find("SmartHouse/Static_Exterior_Ground");
            if (ground != null) ground.GetComponent<MeshRenderer>().sharedMaterial = Mat("M_Ext_Lawn");

            var porch = GameObject.Find("SmartHouse/Static_Exterior_Porch");
            if (porch == null) return;
            var mf = porch.GetComponent<MeshFilter>();
            var mr = porch.GetComponent<MeshRenderer>();
            string path = $"{GeneratedFolder}/Exterior_Paths.asset";
            if (mf.sharedMesh.subMeshCount == 1 && AssetDatabase.GetAssetPath(mf.sharedMesh) == path) return;

            // Submesh 0 is the stepping-stone path; 1 and 2 are the low-poly tree crowns and trunks.
            var src = mf.sharedMesh;
            var mesh = new Mesh { name = "Exterior_Paths" };
            mesh.vertices = src.vertices;
            mesh.normals = src.normals;
            mesh.tangents = src.tangents;
            mesh.uv = src.uv;
            if (src.uv2 != null && src.uv2.Length > 0) mesh.uv2 = src.uv2;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(src.GetTriangles(0), 0);
            mesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            Undo.RecordObjects(new Object[] { mf, mr }, "Remove placeholder trees");
            mf.sharedMesh = mesh;
            mr.sharedMaterials = new[] { mr.sharedMaterials[0] };
        }

        static void BuildGround(Transform parent)
        {
            // A lawn that continues to the horizon around the house's 40 x 40 m slab (a ring, so nothing overlaps).
            var b = new MeshBuilder();
            const float inner = 20f, outer = 260f, y = GroundY - 0.004f;
            b.Quad("M_Ext_Meadow", new Vector3(-outer, y, inner), new Vector3(outer, y, outer));
            b.Quad("M_Ext_Meadow", new Vector3(-outer, y, -outer), new Vector3(outer, y, -inner));
            b.Quad("M_Ext_Meadow", new Vector3(-outer, y, -inner), new Vector3(-inner, y, inner));
            b.Quad("M_Ext_Meadow", new Vector3(inner, y, -inner), new Vector3(outer, y, inner));
            b.Build("Far Lawn", parent, castShadows: false);
        }

        static void BuildStreet(Transform parent)
        {
            var b = new MeshBuilder();
            const float len = 260f;
            // Road (7 m wide), with pavements raised 7 cm on both sides and a short path from the stepping stones.
            b.Box("M_Ext_Asphalt", new Vector3(0, GroundY - 0.02f, 20.5f), new Vector3(len * 2, 0.05f, 7f));
            for (float x = -len; x < len; x += 9f)
                b.Box("M_Ext_RoadPaint", new Vector3(x, GroundY + 0.006f, 20.5f), new Vector3(3f, 0.01f, 0.12f));
            b.Box("M_Ext_RoadPaint", new Vector3(0, GroundY + 0.006f, 17.35f), new Vector3(len * 2, 0.01f, 0.1f));
            b.Box("M_Ext_RoadPaint", new Vector3(0, GroundY + 0.006f, 23.65f), new Vector3(len * 2, 0.01f, 0.1f));
            b.Box("M_Ext_Pavers", new Vector3(0, GroundY + 0.02f, 15.6f), new Vector3(len * 2, 0.16f, 2.8f));
            b.Box("M_Ext_Pavers", new Vector3(0, GroundY + 0.02f, 25.4f), new Vector3(len * 2, 0.16f, 2.8f));
            b.Box("M_Concrete", new Vector3(0, GroundY + 0.03f, 17.05f), new Vector3(len * 2, 0.18f, 0.16f));
            b.Box("M_Concrete", new Vector3(0, GroundY + 0.03f, 23.95f), new Vector3(len * 2, 0.18f, 0.16f));
            b.Box("M_Ext_Pavers", new Vector3(3.25f, GroundY + 0.01f, 11.85f), new Vector3(1.5f, 0.06f, 4.9f));
            b.Build("Road and Pavements", parent);

            // Street lamps: a slim pole with a flat head over the road.
            var lamps = new MeshBuilder();
            foreach (var (x, z, dir) in new[] { (-34f, 14.6f, 1f), (-12f, 14.6f, 1f), (12f, 14.6f, 1f), (34f, 14.6f, 1f),
                                                 (-23f, 26.4f, -1f), (0f, 26.4f, -1f), (23f, 26.4f, -1f), (46f, 26.4f, -1f) })
            {
                lamps.Box("M_Metal_Black", new Vector3(x, GroundY + 0.1f + 2.75f, z), new Vector3(0.12f, 5.5f, 0.12f));
                lamps.Box("M_Metal_Black", new Vector3(x, GroundY + 0.1f + 0.4f, z), new Vector3(0.2f, 0.8f, 0.2f));
                lamps.Box("M_Metal_Black", new Vector3(x, GroundY + 5.55f, z + dir * 0.55f), new Vector3(0.08f, 0.08f, 1.1f));
                lamps.Box("M_Metal_Black", new Vector3(x, GroundY + 5.55f, z + dir * 1.2f), new Vector3(0.3f, 0.1f, 0.6f));
                lamps.Box("M_Ext_LampGlow", new Vector3(x, GroundY + 5.49f, z + dir * 1.2f), new Vector3(0.24f, 0.03f, 0.5f));
            }
            lamps.Build("Street Lamps", parent);

            Place(parent, "utility_box_02", null, new Vector3(13.3f, GroundY + 0.1f, 14.75f), 180f, 1f);
        }

        static void BuildFence(Transform parent)
        {
            // Horizontal-slat timber fence around the garden, open where the path meets the pavement.
            var b = new MeshBuilder();
            const float x0 = -14f, x1 = 14f, zBack = -12f, zFront = 14.0f;
            Run(b, new Vector3(x0, 0, zFront), new Vector3(2.45f, 0, zFront), 0.95f);
            Run(b, new Vector3(4.05f, 0, zFront), new Vector3(x1, 0, zFront), 0.95f);
            Run(b, new Vector3(x1, 0, zFront), new Vector3(x1, 0, zBack), 1.6f);
            Run(b, new Vector3(x1, 0, zBack), new Vector3(x0, 0, zBack), 1.6f);
            Run(b, new Vector3(x0, 0, zBack), new Vector3(x0, 0, zFront), 1.6f);
            b.Build("Fence", parent);
        }

        static void Run(MeshBuilder b, Vector3 from, Vector3 to, float height)
        {
            var dir = to - from;
            float len = dir.magnitude;
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0, 90, 0);   // local x along the run
            int posts = Mathf.CeilToInt(len / 2.4f);
            for (int i = 0; i <= posts; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)posts);
                b.Box("M_Wood_Cladding", new Vector3(p.x, GroundY + height / 2f, p.z), new Vector3(0.09f, height + 0.05f, 0.09f), rot);
            }
            int boards = Mathf.RoundToInt(height / 0.16f);
            for (int k = 0; k < boards; k++)
            {
                float y = GroundY + 0.07f + k * (height - 0.07f) / boards + 0.06f;
                var mid = (from + to) / 2f + rot * new Vector3(0, 0, 0.06f);
                b.Box("M_Wood_Cladding", new Vector3(mid.x, y, mid.z), new Vector3(len + 0.09f, 0.11f, 0.022f), rot);
            }
        }

        static void BuildNeighbours(Transform parent)
        {
            // Across the street (facing the house), either side of the garden, and the backs of the next street.
            House(parent, "House Across 1", new Vector3(-17f, 0, 32f), 180f, 11f, 9f, 3.3f, 0, "M_Wall_White", "M_Wood_Cladding");
            House(parent, "House Across 2", new Vector3(0.5f, 0, 33f), 180f, 12f, 9.5f, 3.1f, 2.9f, "M_Ext_Render_Warm", "M_Exterior_Dark");
            House(parent, "House Across 3", new Vector3(17.5f, 0, 32f), 180f, 10f, 8.5f, 3.2f, 2.8f, "M_Ext_Brick_Red", "M_Wood_Cladding");
            House(parent, "House Across 4", new Vector3(-35f, 0, 33f), 180f, 11f, 9f, 3.2f, 2.8f, "M_Wall_White", "M_Exterior_Dark");
            House(parent, "House Across 5", new Vector3(35f, 0, 32.5f), 180f, 11f, 9f, 3.3f, 0, "M_Ext_Render_Warm", "M_Wood_Cladding");
            House(parent, "House Side West", new Vector3(-27f, 0, 9f), 0f, 11f, 10f, 3.2f, 2.8f, "M_Ext_Brick_Red", "M_Wood_Cladding");
            House(parent, "House Side East", new Vector3(27f, 0, 9f), 0f, 10f, 10f, 3.3f, 0, "M_Wall_White", "M_Exterior_Dark");
            House(parent, "House Behind 1", new Vector3(-9f, 0, -32f), 180f, 12f, 9f, 3.2f, 2.8f, "M_Ext_Render_Warm", "M_Wood_Cladding");
            House(parent, "House Behind 2", new Vector3(11f, 0, -33f), 180f, 11f, 9f, 3.3f, 0, "M_Ext_Brick_Red", "M_Exterior_Dark");
        }

        /// <summary>A simple modern house; <paramref name="front"/> is the centre of the front facade at ground level.</summary>
        static void House(Transform parent, string name, Vector3 front, float yaw, float w, float d, float h, float upper, string wall, string accent)
        {
            var b = new MeshBuilder();
            float y0 = GroundY;
            // Local space: x across the facade, +z out of the front, body behind z = 0.
            b.Box("M_Concrete", new Vector3(0, y0 + 0.15f, -d / 2), new Vector3(w + 0.1f, 0.3f, d + 0.1f));
            b.Box(wall, new Vector3(0, y0 + h / 2, -d / 2), new Vector3(w, h, d));
            b.Box(accent, new Vector3(w * 0.3f, y0 + h / 2, 0.04f), new Vector3(w * 0.36f, h - 0.1f, 0.08f));
            b.Box("M_Exterior_Dark", new Vector3(0, y0 + h + 0.14f, -d / 2), new Vector3(w + 0.7f, 0.28f, d + 0.7f));
            b.Box("M_Wood_Dark", new Vector3(w * 0.3f, y0 + 0.3f + 1.05f, 0.1f), new Vector3(1.0f, 2.1f, 0.06f));
            Window(b, new Vector3(-w * 0.22f, y0 + 1.55f, 0), 2.6f, 1.9f);
            Window(b, new Vector3(w * 0.06f, y0 + 1.75f, 0), 1.2f, 1.4f);
            // Side windows.
            Window(b, new Vector3(-w / 2, y0 + 1.6f, -d * 0.35f), 1.6f, 1.3f, Quaternion.Euler(0, -90, 0));
            Window(b, new Vector3(w / 2, y0 + 1.6f, -d * 0.6f), 1.6f, 1.3f, Quaternion.Euler(0, 90, 0));
            Window(b, new Vector3(-w * 0.2f, y0 + 1.6f, -d), 2.4f, 1.5f, Quaternion.Euler(0, 180, 0));
            if (upper > 0)
            {
                float uw = w * 0.62f, ud = d * 0.75f, ux = -w * 0.17f, uy = y0 + h + 0.28f;
                b.Box(accent == "M_Exterior_Dark" ? "M_Exterior_Dark" : wall, new Vector3(ux, uy + upper / 2, -ud / 2 - 0.3f), new Vector3(uw, upper, ud));
                b.Box("M_Exterior_Dark", new Vector3(ux, uy + upper + 0.12f, -ud / 2 - 0.3f), new Vector3(uw + 0.6f, 0.24f, ud + 0.6f));
                Window(b, new Vector3(ux - uw * 0.2f, uy + upper * 0.5f, -0.3f), 2.2f, 1.4f);
                Window(b, new Vector3(ux + uw * 0.28f, uy + upper * 0.5f, -0.3f), 1.0f, 1.4f);
                Window(b, new Vector3(ux, uy + upper * 0.5f, -ud - 0.3f), 1.8f, 1.2f, Quaternion.Euler(0, 180, 0));
            }
            // Driveway out to the street and a paved front path.
            b.Box("M_Ext_Pavers", new Vector3(-w * 0.32f, y0 + 0.01f, 3.6f), new Vector3(3.2f, 0.04f, 7.2f));
            b.Box("M_Ext_Pavers", new Vector3(w * 0.3f, y0 + 0.01f, 3.6f), new Vector3(1.2f, 0.04f, 7.2f));

            var go = b.Build(name, parent);
            go.transform.SetPositionAndRotation(front, Quaternion.Euler(0, yaw, 0));
        }

        static void Window(MeshBuilder b, Vector3 c, float w, float h, Quaternion? r = null)
        {
            var rot = r ?? Quaternion.identity;
            const float f = 0.07f, depth = 0.05f;
            b.Box("M_Ext_WindowDark", c + rot * new Vector3(0, 0, 0.01f), new Vector3(w, h, 0.03f), rot);
            b.Box("M_Metal_Black", c + rot * new Vector3(0, h / 2, depth / 2), new Vector3(w + f, f, depth), rot);
            b.Box("M_Metal_Black", c + rot * new Vector3(0, -h / 2, depth / 2), new Vector3(w + f, f, depth), rot);
            b.Box("M_Metal_Black", c + rot * new Vector3(-w / 2, 0, depth / 2), new Vector3(f, h, depth), rot);
            b.Box("M_Metal_Black", c + rot * new Vector3(w / 2, 0, depth / 2), new Vector3(f, h, depth), rot);
            if (w > 1.5f) b.Box("M_Metal_Black", c + rot * new Vector3(0, 0, depth / 2), new Vector3(0.05f, h, depth), rot);
        }

        static void BuildGarden(Transform parent, Random rnd)
        {
            // Mulched planting beds along the glazing (low plants only, so the view stays open).
            var beds = new MeshBuilder();
            beds.Box("M_Ext_Mulch", new Vector3(-2.4f, GroundY + 0.005f, 4.75f), new Vector3(4.2f, 0.03f, 1.2f));
            beds.Box("M_Ext_Mulch", new Vector3(-5.6f, GroundY + 0.005f, 0.5f), new Vector3(1.1f, 0.03f, 7.6f));
            beds.Box("M_Ext_Mulch", new Vector3(2.8f, GroundY + 0.005f, -4.6f), new Vector3(4.4f, 0.03f, 1.0f));
            beds.Build("Planting Beds", parent, castShadows: false);

            void Scatter(string asset, int count, Vector3 min, Vector3 max, float s0, float s1)
            {
                for (int i = 0; i < count; i++)
                {
                    var p = new Vector3(Lerp(rnd, min.x, max.x), GroundY, Lerp(rnd, min.z, max.z));
                    Place(parent, asset, null, p, Lerp(rnd, 0, 360), Lerp(rnd, s0, s1), rnd);
                }
            }

            Scatter("flower_gazania", 14, new Vector3(-4.3f, 0, 4.3f), new Vector3(-0.5f, 0, 5.2f), 1.2f, 1.6f);
            Scatter("fern_02", 3, new Vector3(-4.3f, 0, 4.4f), new Vector3(-0.6f, 0, 5.2f), 0.8f, 1f);
            Scatter("flower_gazania", 12, new Vector3(-6f, 0, -3.2f), new Vector3(-5.3f, 0, 4.1f), 1.2f, 1.6f);
            Scatter("fern_02", 4, new Vector3(-6f, 0, -3.2f), new Vector3(-5.3f, 0, 4.1f), 0.8f, 1.1f);
            Scatter("wild_rooibos_bush", 3, new Vector3(-6f, 0, -3.2f), new Vector3(-5.4f, 0, -1.4f), 0.9f, 1.1f);
            Scatter("shrub_04", 4, new Vector3(0.9f, 0, -5f), new Vector3(4.8f, 0, -4.3f), 1.4f, 1.8f);
            Scatter("flower_gazania", 6, new Vector3(0.9f, 0, -5f), new Vector3(4.8f, 0, -4.3f), 1.2f, 1.5f);

            // Larger shrubs where the garden meets the fence, and smaller ones by the house corners.
            var shrubs = new[] { new Vector2(-12.7f, -10.7f), new Vector2(-10.6f, -11f), new Vector2(12.7f, -10.6f), new Vector2(10.8f, -11f),
                                 new Vector2(12.9f, -2.5f), new Vector2(12.8f, 0.6f), new Vector2(-12.9f, -4.5f), new Vector2(-12.8f, 6.2f),
                                 new Vector2(5.5f, -11.1f), new Vector2(-3f, -11.2f), new Vector2(12.6f, 12.6f), new Vector2(-12.6f, 12.7f),
                                 new Vector2(8.5f, 13.0f), new Vector2(-7.5f, 13.0f) };
            foreach (var s in shrubs)
                Place(parent, "shrub_02", null, new Vector3(s.x, GroundY, s.y), Lerp(rnd, 0, 360), Lerp(rnd, 0.9f, 1.2f), rnd);
            foreach (var s in new[] { new Vector2(-5.9f, -3.9f), new Vector2(-5.9f, 4.5f), new Vector2(5.9f, -4.6f), new Vector2(6.1f, 2.6f), new Vector2(0.6f, -4.9f), new Vector2(5.6f, 4.7f) })
                Place(parent, "searsia_lucida", new[] { "searsia_lucida_d", "searsia_lucida_e", "searsia_lucida_f", "searsia_lucida_g" }[rnd.Next(4)],
                      new Vector3(s.x, GroundY, s.y), Lerp(rnd, 0, 360), Lerp(rnd, 0.9f, 1.1f));

            // Rocks and a stump to break up the lawn.
            Place(parent, "rock_moss_set_02", null, new Vector3(-9.2f, GroundY, 3.3f), 30f, 0.8f, rnd);
            Place(parent, "rock_moss_set_02", null, new Vector3(-8.2f, GroundY, -7.6f), 140f, 0.7f, rnd);
            Place(parent, "rock_moss_set_02", null, new Vector3(9.6f, GroundY, -7.8f), 250f, 0.75f, rnd);
            Place(parent, "rock_moss_set_02", null, new Vector3(-7.6f, GroundY, 9.6f), 80f, 0.6f, rnd);
            Place(parent, "boulder_01", null, new Vector3(7.8f, GroundY, 10.4f), 60f, 0.7f);
            Place(parent, "tree_stump_01", null, new Vector3(11.6f, GroundY, -4.2f), 20f, 0.9f);

            // Potted plants either side of the front door, and clay pots on the stepping stones.
            Place(parent, "potted_plant_02", null, new Vector3(2.35f, 0.5f, 4.45f), 10f, 1.1f);
            Place(parent, "potted_plant_02", null, new Vector3(4.3f, 0.5f, 4.45f), 200f, 1.1f);
            Place(parent, "planter_pot_clay", null, new Vector3(4.65f, 0.5f, 5.3f), 0f, 1.6f);
        }

        static void BuildTrees(Transform parent, Random rnd)
        {
            // (tree, position, height in metres)
            var garden = new (string, Vector2, float)[]
            {
                ("island_tree_02", new Vector2(-10.2f, 1.2f), 7.5f),   // framed by the west window
                ("jacaranda_tree", new Vector2(8.5f, -9.2f), 10.5f),   // seen from the kitchen window
                ("tree_small_02", new Vector2(-9.6f, -8.2f), 6f),
                ("tree_small_02", new Vector2(10.4f, 7.2f), 5.5f),
                ("island_tree_02", new Vector2(-10.6f, 10.6f), 6.5f),
                // Street trees on the far pavement and in the neighbours' gardens.
                ("tree_small_02", new Vector2(-29f, 28.2f), 7f),
                ("island_tree_02", new Vector2(-9f, 28.4f), 7.5f),
                ("tree_small_02", new Vector2(9f, 28.2f), 6.5f),
                ("island_tree_02", new Vector2(27f, 28.4f), 8f),
                ("jacaranda_tree", new Vector2(-21f, -22f), 12f),
                ("jacaranda_tree", new Vector2(26f, -18f), 11f),
                ("tree_small_02", new Vector2(-24f, 18.5f), 6f),
                ("island_tree_02", new Vector2(22f, 1f), 7f),
            };
            foreach (var (tree, p, height) in garden)
                PlaceTree(parent, tree, new Vector3(p.x, GroundY - 0.05f, p.y), Lerp(rnd, 0, 360), height, true);
        }

        static void BuildTreeLine(Transform parent, Random rnd)
        {
            // Impostor-only trees: four quads each, so a few hundred are cheap. Kept off the road and houses.
            int placed = 0, guard = 0;
            while (placed < 260 && guard++ < 5000)
            {
                float a = Lerp(rnd, 0, Mathf.PI * 2);
                float r = Mathf.Sqrt(Lerp(rnd, 38f * 38f, 150f * 150f));
                var p = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                if (Mathf.Abs(p.y - 20.5f) < 9f) continue;                                   // road and pavements
                if (Mathf.Abs(p.x) < 45f && p.y > 26f && p.y < 46f) continue;                // houses across the street
                if (Mathf.Abs(p.x) < 25f && p.y < -26f && p.y > -42f) continue;              // houses behind
                string tree = Trees[rnd.Next(Trees.Length)];
                var go = PlaceTree(parent, tree, new Vector3(p.x, GroundY - 0.1f, p.y), Lerp(rnd, 0, 360), Lerp(rnd, 8f, 15f), false);
                placed++;
            }
        }

        static GameObject PlaceTree(Transform parent, string tree, Vector3 pos, float yaw, float height, bool full)
        {
            string dir = $"{ModelRoot}/{tree}";
            GameObject go;
            if (full)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Tree_{tree}.prefab");
                if (prefab == null) return null;
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                var imp = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{tree}_Impostor.fbx");
                if (imp == null) return null;
                go = (GameObject)PrefabUtility.InstantiatePrefab(imp, parent);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/M_{tree}_impostor.mat");
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            // Scale to the requested height using the full mesh's (or impostor's) bounds.
            var b = RendererBounds(full ? go.transform.Find("LOD0").gameObject : go);
            float s = b.size.y > 0.01f ? height / b.size.y : 1f;
            go.transform.localScale = Vector3.one * s;
            return go;
        }

        /// <summary>Places one mesh of an asset (a named variant, a random one, or all), sitting on the ground.</summary>
        static GameObject Place(Transform parent, string asset, string variant, Vector3 pos, float yaw, float scale, Random rnd = null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{asset}/{asset}.fbx");
            if (model == null) { Debug.LogWarning($"[SmartHouse] Missing model {asset}"); return null; }
            var parts = model.GetComponentsInChildren<MeshFilter>(true);
            var picks = new List<MeshFilter>();
            if (variant != null) picks.AddRange(Array.FindAll(parts, p => p.name == variant));
            else if (rnd != null && parts.Length > 1 && !asset.StartsWith("potted")) picks.Add(parts[rnd.Next(parts.Length)]);
            else picks.AddRange(parts);
            if (picks.Count == 0) return null;

            var go = new GameObject(picks.Count == 1 ? picks[0].name : asset);
            go.transform.SetParent(parent, false);
            foreach (var mf in picks)
            {
                var child = picks.Count == 1 ? go : new GameObject(mf.name);
                if (child != go) child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                child.AddComponent<MeshRenderer>().sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
                // Keep the part's orientation/scale from the model, but not its layout offset (only matters for single picks).
                if (child != go) child.transform.localPosition = model.transform.InverseTransformPoint(mf.transform.position);
                child.transform.localRotation = mf.transform.rotation;
                child.transform.localScale = mf.transform.lossyScale;
            }

            // Wrap so yaw/scale apply around the base centre, then drop it onto the ground.
            var wrapper = new GameObject(go.name);
            wrapper.transform.SetParent(parent, false);
            var bounds = RendererBounds(go);
            go.transform.SetParent(wrapper.transform, true);
            go.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            go.name = "Mesh";
            wrapper.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            wrapper.transform.localScale = Vector3.one * scale;

            float ground = GroundY;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Transform>().root.name == "SmartHouse")
                ground = hit.point.y;
            wrapper.transform.position = new Vector3(pos.x, ground - 0.02f * scale, pos.z);
            return wrapper;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static float Lerp(Random rnd, float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        static string Unix(string p) => p.Replace('\\', '/');
        static IEnumerable<string> Concat(this string[] a, string b) { foreach (var s in a) yield return s; yield return b; }

        /// <summary>Box meshes with UVs in metres (one texture tile per metre before material tiling), grouped by material.</summary>
        class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly Dictionary<string, List<int>> tris = new Dictionary<string, List<int>>();

            List<int> T(string mat)
            {
                if (!tris.TryGetValue(mat, out var t)) tris[mat] = t = new List<int>();
                return t;
            }

            public void Quad(string mat, Vector3 min, Vector3 max)
            {
                int i = v.Count;
                v.Add(new Vector3(min.x, min.y, min.z)); v.Add(new Vector3(min.x, min.y, max.z));
                v.Add(new Vector3(max.x, min.y, max.z)); v.Add(new Vector3(max.x, min.y, min.z));
                for (int k = 0; k < 4; k++) { n.Add(Vector3.up); uv.Add(new Vector2(v[i + k].x, v[i + k].z)); }
                T(mat).AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Box(string mat, Vector3 c, Vector3 size, Quaternion? r = null)
            {
                var rot = r ?? Quaternion.identity;
                var h = size / 2f;
                var t = T(mat);
                // Each face: normal, then the two in-plane axes (u, v) scaled by the half extents.
                var faces = new[]
                {
                    (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.back, Vector3.up),
                    (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.right, Vector3.back),
                    (Vector3.forward, Vector3.left, Vector3.up), (Vector3.back, Vector3.right, Vector3.up),
                };
                foreach (var (fn, fu, fv) in faces)
                {
                    var nn = Vector3.Scale(fn, h); var uu = Vector3.Scale(fu, h); var vv = Vector3.Scale(fv, h);
                    float lu = uu.magnitude, lv = vv.magnitude;
                    int i = v.Count;
                    var corners = new[] { nn - uu - vv, nn + uu - vv, nn + uu + vv, nn - uu + vv };
                    var cuv = new[] { new Vector2(-lu, -lv), new Vector2(lu, -lv), new Vector2(lu, lv), new Vector2(-lu, lv) };
                    for (int k = 0; k < 4; k++)
                    {
                        v.Add(c + rot * corners[k]);
                        n.Add(rot * fn);
                        // Offset by the box centre so neighbouring boxes don't all start their texture at the same spot.
                        uv.Add(cuv[k] + new Vector2(Vector3.Dot(c, rot * fu), Vector3.Dot(c, rot * fv)));
                    }
                    t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                }
            }

            public GameObject Build(string name, Transform parent, bool castShadows = true)
            {
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
                mesh.subMeshCount = tris.Count;
                var mats = new List<Material>();
                int s = 0;
                foreach (var kv in tris)
                {
                    mesh.SetTriangles(kv.Value, s++);
                    var m = Mat(kv.Key);
                    if (m == null) Debug.LogWarning($"[SmartHouse] Missing material {kv.Key}");
                    mats.Add(m);
                }
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                string path = $"{GeneratedFolder}/{Sanitize(parent.name)}_{Sanitize(name)}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);

                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats.ToArray();
                mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                return go;
            }

            static string Sanitize(string s) => s.Replace(' ', '_');
        }
    }
}
