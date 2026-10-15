using UnityEngine;

namespace SmartHouse
{
    /// <summary>Procedurally generated UI sprites (anti-aliased rounded rectangle and circle).</summary>
    public static class UiSprites
    {
        static Sprite rounded;
        static Sprite circle;

        public static Sprite Rounded => rounded != null ? rounded : rounded = Create(64, 24f, new Vector4(26, 26, 26, 26));
        public static Sprite Circle => circle != null ? circle : circle = Create(64, 32f, Vector4.zero);

        static Sprite Create(int size, float radius, Vector4 border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Signed distance to a rounded square filling the texture.
                float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                byte a = (byte)(Mathf.Clamp01(0.5f - outside) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
