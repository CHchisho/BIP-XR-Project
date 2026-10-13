using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartHouse
{
    /// <summary>
    /// Floating, camera-facing status pill shown above a device:
    /// a coloured state dot, the device name and its current state.
    /// Fades out with distance and pops briefly when the state changes.
    /// </summary>
    public class StatusLabel : MonoBehaviour
    {
        [SerializeField] SmartDevice device;
        [SerializeField] string titleOverride;
        [SerializeField] float showDistance = 7f;
        [SerializeField] float fadeDistance = 2f;
        [Tooltip("Label sits below the device (e.g. a pendant lamp) instead of above it.")]
        [SerializeField] bool hangBelow;

        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.1f, 0.78f);
        static readonly Color TitleColor = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color InactiveDot = new Color(0.55f, 0.58f, 0.62f);

        CanvasGroup group;
        Image dot;
        Image dotGlow;
        TMP_Text title;
        TMP_Text state;
        RectTransform panel;
        float pop;

        public void Bind(SmartDevice target, string title = null)
        {
            device = target;
            titleOverride = title;
        }

        void Start()
        {
            Build();
            if (device != null)
            {
                device.StateChanged += _ => { Refresh(); pop = 1f; };
                Refresh();
            }
        }

        void LateUpdate()
        {
            if (device == null || group == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            // Billboard around the vertical axis so the text stays upright.
            Vector3 toCamera = transform.position - cam.transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toCamera);

            float distance = Vector3.Distance(cam.transform.position, transform.position);
            float alpha = Mathf.Clamp01((showDistance - distance) / fadeDistance);
            group.alpha = Mathf.MoveTowards(group.alpha, alpha, Time.deltaTime * 4f);

            // Grow a little with distance so the label stays readable across the room.
            float distanceScale = Mathf.Clamp(distance * 0.6f, 1.4f, 3f);
            pop = Mathf.MoveTowards(pop, 0f, Time.deltaTime * 4f);
            panel.localScale = Vector3.one * distanceScale * (1f + 0.12f * Mathf.Sin(pop * Mathf.PI));

            // Animated states (e.g. curtain moving) update continuously.
            if (Time.frameCount % 10 == 0) Refresh();
        }

        void Refresh()
        {
            if (state == null || device == null) return;
            title.text = string.IsNullOrEmpty(titleOverride) ? device.DisplayName : titleOverride;
            state.text = device.Status;
            var accent = device.IsActive ? device.AccentColor : InactiveDot;
            dot.color = accent;
            dotGlow.color = new Color(accent.r, accent.g, accent.b, device.IsActive ? 0.35f : 0f);
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // 1 canvas unit = 1 mm, so sizes below read as millimetres.
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(200f, 60f);
            canvasRect.localScale = Vector3.one * 0.001f;

            panel = NewRect("Panel", canvasRect);
            panel.pivot = new Vector2(0.5f, hangBelow ? 1f : 0f); // grow away from the device
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = PanelColor;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var row = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(14, 20, 9, 10);
            row.spacing = 11f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var dotHolder = NewRect("Dot", panel);
            var dotLayout = dotHolder.gameObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = dotLayout.preferredHeight = 14f;
            dotGlow = NewRect("Glow", dotHolder).gameObject.AddComponent<Image>();
            dotGlow.sprite = UiSprites.Circle;
            Stretch(dotGlow.rectTransform, -7f);
            dot = NewRect("Fill", dotHolder).gameObject.AddComponent<Image>();
            dot.sprite = UiSprites.Circle;
            Stretch(dot.rectTransform, 0f);

            var texts = NewRect("Texts", panel);
            var column = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = -2f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            title = NewText("Title", texts, 11f, FontStyles.Normal, TitleColor);
            title.characterSpacing = 4f;
            state = NewText("State", texts, 17f, FontStyles.Bold, Color.white);

            // Lay out immediately so the label never shows a frame at default (100x100) sizes.
            dotHolder.sizeDelta = new Vector2(14f, 14f);
            Refresh();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }

        static TMP_Text NewText(string name, RectTransform parent, float size, FontStyles style, Color color)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }

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
