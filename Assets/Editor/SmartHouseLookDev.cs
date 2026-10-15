using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SmartHouse.EditorTools
{
    /// <summary>
    /// Generates tileable procedural textures (albedo + normal) and realistic URP materials
    /// for the imported SmartHouse model, and remaps the FBX materials to them.
    /// The model's UVs are box-projected in metres, so 1 texture tile = 1 metre.
    /// </summary>
    public static class SmartHouseLookDev
    {
        const string ModelPath = "Assets/Models/SmartHouse.fbx";
        const string TextureFolder = "Assets/Materials/Textures";
        const string MaterialFolder = "Assets/Materials/House";

        [MenuItem("Smart House/Rebuild Materials")]
        public static void RebuildMaterials()
        {
            Directory.CreateDirectory(TextureFolder);
            Directory.CreateDirectory(MaterialFolder);

            // Colours are defined here (not read from the FBX) because after the first run the
            // FBX materials are remapped and their original values are no longer available.
            var specs = new List<Spec>
            {
                new Spec("M_Floor_Oak", Pattern.Planks, new Color(0.72f, 0.55f, 0.37f), 0.42f, 0f, 0.7f, 1024),
                new Spec("M_Wood_Dark", Pattern.Grain, new Color(0.36f, 0.26f, 0.19f), 0.5f, 0f, 0.4f, 512),
                new Spec("M_Wood_Cladding", Pattern.Boards, new Color(0.62f, 0.47f, 0.32f), 0.3f, 0f, 0.8f, 512),
                new Spec("M_Wall_White", Pattern.Plaster, new Color(0.93f, 0.92f, 0.9f), 0.12f, 0f, 0.08f, 512),
                new Spec("M_Concrete", Pattern.Concrete, new Color(0.62f, 0.62f, 0.6f), 0.22f, 0f, 0.6f, 512),
                new Spec("M_Exterior_Dark", Pattern.Concrete, new Color(0.22f, 0.22f, 0.23f), 0.25f, 0f, 0.6f, 512),
                new Spec("M_Fabric_Grey", Pattern.Fabric, new Color(0.45f, 0.46f, 0.48f), 0.05f, 0f, 0.9f, 512),
                new Spec("M_Shade_Fabric", Pattern.Fabric, new Color(0.92f, 0.9f, 0.86f), 0.05f, 0f, 0.5f, 512),
                new Spec("M_Fabric_Rug", Pattern.Rug, new Color(0.8f, 0.77f, 0.72f), 0.02f, 0f, 1f, 512),
                new Spec("M_Grass", Pattern.Grass, new Color(0.33f, 0.5f, 0.24f), 0.08f, 0f, 1f, 512),
                new Spec("M_Countertop", Pattern.Stone, new Color(0.92f, 0.92f, 0.9f), 0.78f, 0f, 0.15f, 512),
                new Spec("M_Plant", Pattern.Grass, new Color(0.2f, 0.42f, 0.18f), 0.35f, 0f, 0.6f, 256),
                new Spec("M_Cabinet", Pattern.None, new Color(0.36f, 0.4f, 0.37f), 0.55f, 0f, 0f, 0),
                new Spec("M_Metal_Black", Pattern.None, new Color(0.06f, 0.06f, 0.065f), 0.62f, 0.9f, 0f, 0),
                new Spec("M_Device_White", Pattern.None, new Color(0.93f, 0.93f, 0.92f), 0.65f, 0f, 0f, 0),
                new Spec("M_Device_Black", Pattern.None, new Color(0.08f, 0.08f, 0.085f), 0.7f, 0f, 0f, 0),
                new Spec("M_Screen", Pattern.None, new Color(0.02f, 0.02f, 0.025f), 0.95f, 0f, 0f, 0),
                new Spec("M_Lamp_Emissive", Pattern.None, new Color(1f, 0.95f, 0.85f), 0.5f, 0f, 0f, 0),
            };

            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var lit = Shader.Find("Universal Render Pipeline/Lit");

            foreach (var spec in specs)
            {
                string matPath = $"{MaterialFolder}/{spec.Name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(lit) { name = spec.Name };
                    AssetDatabase.CreateAsset(mat, matPath);
                }

                mat.SetColor("_BaseColor", spec.Pattern == Pattern.None ? spec.Color : Color.white);
                mat.SetFloat("_Smoothness", spec.Smoothness);
                mat.SetFloat("_Metallic", spec.Metallic);

                if (spec.Pattern != Pattern.None)
                {
                    GenerateTextures(spec, out var albedo, out var normal);
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetTexture("_BumpMap", normal);
                    mat.SetFloat("_BumpScale", 1f);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (spec.Name == "M_Lamp_Emissive")
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }

                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.Name), mat);
            }

            // Lightmap UVs for baked global illumination.
            importer.generateSecondaryUV = true;
            importer.secondaryUVHardAngle = 88f;
            importer.secondaryUVPackMargin = 8f;
            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();
            Debug.Log("[SmartHouse] Materials rebuilt.");
        }

        enum Pattern { None, Planks, Grain, Boards, Plaster, Concrete, Fabric, Rug, Grass, Stone }

        class Spec
        {
            public readonly string Name;
            public readonly Pattern Pattern;
            public readonly Color Color;
            public readonly float Smoothness;
            public readonly float Metallic;
            public readonly float Bump;
            public readonly int Size;

            public Spec(string name, Pattern pattern, Color color, float smoothness, float metallic, float bump, int size)
            {
                Name = name; Pattern = pattern; Color = color; Smoothness = smoothness; Metallic = metallic; Bump = bump; Size = size;
            }
        }

        static void GenerateTextures(Spec spec, out Texture2D albedoAsset, out Texture2D normalAsset)
        {
            int n = spec.Size;
            var height = new float[n * n];
            var albedo = new Color[n * n];
            int seed = spec.Name.GetHashCode() & 0xffff;

            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (float)x / n, v = (float)y / n;
                Sample(spec.Pattern, u, v, seed, out float shade, out float h, out Color tint);
                height[y * n + x] = h;
                var c = spec.Color * tint * shade;
                c.a = 1f;
                albedo[y * n + x] = c;
            }

            var normal = new Color[n * n];
            float strength = spec.Bump * 6f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float l = height[y * n + (x - 1 + n) % n], r = height[y * n + (x + 1) % n];
                float d = height[((y - 1 + n) % n) * n + x], t = height[((y + 1) % n) * n + x];
                var nrm = new Vector3((l - r) * strength, (d - t) * strength, 1f).normalized;
                normal[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
            }

            albedoAsset = Save($"{TextureFolder}/{spec.Name}_Albedo.png", n, albedo, false);
            normalAsset = Save($"{TextureFolder}/{spec.Name}_Normal.png", n, normal, true);
        }

        static Texture2D Save(string path, int size, Color[] pixels, bool isNormal)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, isNormal);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !isNormal;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 8;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- Patterns (all tile seamlessly over u,v in [0,1)) ----------

        static void Sample(Pattern pattern, float u, float v, int seed, out float shade, out float height, out Color tint)
        {
            tint = Color.white;
            switch (pattern)
            {
                case Pattern.Planks:
                {
                    // 5 planks per metre, each split once along its length at a staggered position.
                    const int planks = 5;
                    int plank = Mathf.FloorToInt(u * planks);
                    float across = u * planks - plank;
                    float jointPos = Hash(plank, 7, seed);
                    float along = Frac(v - jointPos);
                    int board = plank * 3 + (v - jointPos < 0f || v - jointPos >= 1f ? 1 : 0) + (along < 0.5f ? 0 : 2);
                    float boardTone = 0.82f + 0.22f * Hash(board, 13, seed);
                    float grain = Fbm(u * planks * 3f + board * 3f, v * 2f, 4, seed, planks * 3, 2);
                    float streaks = 0.5f + 0.5f * Mathf.Sin((across * 14f + grain * 6f) * Mathf.PI);
                    float seam = Mathf.Min(Edge(across, 0.012f), Edge(Frac(along * 2f), 0.006f));
                    shade = boardTone * (0.86f + 0.1f * streaks + 0.08f * grain) * Mathf.Lerp(0.45f, 1f, seam);
                    height = 0.6f * seam + 0.04f * streaks;
                    tint = Color.Lerp(new Color(1f, 0.96f, 0.92f), new Color(1.04f, 1f, 0.95f), Hash(board, 3, seed));
                    break;
                }
                case Pattern.Grain:
                {
                    float grain = Fbm(u * 3f, v * 24f, 5, seed, 3, 24);
                    float rings = 0.5f + 0.5f * Mathf.Sin((v * 40f + grain * 8f) * Mathf.PI);
                    shade = 0.85f + 0.12f * rings + 0.06f * grain;
                    height = 0.05f * rings;
                    break;
                }
                case Pattern.Boards:
                {
                    const int boards = 7;
                    int b = Mathf.FloorToInt(v * boards);
                    float within = v * boards - b;
                    float grain = Fbm(u * 18f, v * 3f + b, 4, seed, 18, 3);
                    float seam = Edge(within, 0.04f);
                    shade = (0.85f + 0.18f * Hash(b, 5, seed)) * (0.9f + 0.12f * grain) * Mathf.Lerp(0.5f, 1f, seam);
                    height = seam + 0.03f * grain;
                    break;
                }
                case Pattern.Plaster:
                {
                    float low = Fbm(u * 3f, v * 3f, 4, seed, 3, 3);
                    float fine = Fbm(u * 64f, v * 64f, 2, seed + 1, 64, 64);
                    shade = 0.965f + 0.03f * low + 0.015f * fine;
                    height = 0.05f * low + 0.25f * fine;
                    break;
                }
                case Pattern.Concrete:
                {
                    float low = Fbm(u * 4f, v * 4f, 5, seed, 4, 4);
                    float pores = Fbm(u * 48f, v * 48f, 2, seed + 3, 48, 48);
                    float pit = pores > 0.78f ? 0.82f : 1f;
                    shade = (0.88f + 0.16f * low) * pit;
                    height = 0.4f * low + (pit < 1f ? -0.3f : 0f);
                    break;
                }
                case Pattern.Fabric:
                {
                    float warp = Mathf.Sin(u * 2f * Mathf.PI * 180f);
                    float weft = Mathf.Sin(v * 2f * Mathf.PI * 180f);
                    float weave = 0.5f + 0.25f * (warp + weft);
                    float mottling = Fbm(u * 6f, v * 6f, 3, seed, 6, 6);
                    shade = 0.88f + 0.08f * weave + 0.08f * mottling;
                    height = weave;
                    break;
                }
                case Pattern.Rug:
                {
                    float knots = Fbm(u * 90f, v * 90f, 2, seed, 90, 90);
                    float pile = Fbm(u * 8f, v * 8f, 3, seed + 2, 8, 8);
                    shade = 0.82f + 0.14f * knots + 0.08f * pile;
                    height = knots;
                    break;
                }
                case Pattern.Grass:
                {
                    float blades = Fbm(u * 70f, v * 70f, 3, seed, 70, 70);
                    float patches = Fbm(u * 3f, v * 3f, 4, seed + 5, 3, 3);
                    shade = 0.7f + 0.35f * blades;
                    tint = Color.Lerp(new Color(0.85f, 1f, 0.75f), new Color(1.12f, 1.05f, 0.8f), patches);
                    height = blades;
                    break;
                }
                case Pattern.Stone:
                {
                    float speck = Fbm(u * 80f, v * 80f, 2, seed, 80, 80);
                    float veins = Mathf.Abs(Fbm(u * 2f, v * 2f, 5, seed + 9, 2, 2) - 0.5f);
                    shade = 0.95f + 0.05f * speck - (speck > 0.8f ? 0.25f : 0f) - (veins < 0.015f ? 0.08f : 0f);
                    height = 0.2f * speck;
                    break;
                }
                default:
                    shade = 1f;
                    height = 0f;
                    break;
            }
        }

        static float Edge(float t, float width) => Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / width);
        static float Frac(float x) => x - Mathf.Floor(x);

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        // Periodic value noise: lattice coordinates wrap at (periodX, periodY) so the texture tiles.
        static float ValueNoise(float x, float y, int seed, int periodX, int periodY)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wx(int i) => ((i % periodX) + periodX) % periodX;
            int Wy(int i) => ((i % periodY) + periodY) % periodY;
            float a = Hash(Wx(x0), Wy(y0), seed), b = Hash(Wx(x0 + 1), Wy(y0), seed);
            float c = Hash(Wx(x0), Wy(y0 + 1), seed), d = Hash(Wx(x0 + 1), Wy(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float x, float y, int octaves, int seed, int periodX, int periodY)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(x, y, seed + o * 31, Mathf.Max(1, periodX), Mathf.Max(1, periodY));
                norm += amp;
                x *= 2f; y *= 2f; periodX *= 2; periodY *= 2; amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
