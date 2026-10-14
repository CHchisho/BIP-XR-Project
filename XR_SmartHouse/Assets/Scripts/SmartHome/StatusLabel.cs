using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartHouse
{
    /// <summary>
    /// Floating, camera-facing status pill shown above a device:
    /// a coloured state dot, the device name and its current state.
    /// Keeps roughly the same apparent size at any distance so it stays readable across the room,
    /// fades out when far away and pops briefly when the state changes.
    /// </summary>
    public class StatusLabel : MonoBehaviour
    {
        [SerializeField] SmartDevice device;
        [SerializeField] string titleOverride;
        [SerializeField] float showDistance = 8f;
        [SerializeField] float fadeDistance = 2f;
        [Tooltip("Label scale per metre of distance; higher = larger on screen.")]
        [SerializeField] float angularSize = 0.9f;
        [Tooltip("Label sits below the device (e.g. a pendant lamp) instead of above it.")]
        [SerializeField] bool hangBelow;

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

            // Scale with distance so the text keeps the same apparent size (about 1 degree for the state line).
            float distanceScale = Mathf.Clamp(distance * angularSize, 0.5f, 5f);
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
            var accent = device.IsActive ? device.AccentColor : UiTheme.InactiveDot;
            dot.color = UiTheme.Ui(accent);
            dotGlow.color = UiTheme.Ui(new Color(accent.r, accent.g, accent.b, device.IsActive ? 0.35f : 0f));
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
            bg.color = UiTheme.Ui(UiTheme.Panel);
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var row = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(16, 24, 10, 12);
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var dotHolder = NewRect("Dot", panel);
            var dotLayout = dotHolder.gameObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = dotLayout.preferredHeight = 16f;
            dotGlow = NewRect("Glow", dotHolder).gameObject.AddComponent<Image>();
            dotGlow.sprite = UiSprites.Circle;
            Stretch(dotGlow.rectTransform, -8f);
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

            title = NewText("Title", texts, 12f, FontStyles.Bold, UiTheme.Ui(UiTheme.TextDim));
            title.characterSpacing = 3f;
            state = NewText("State", texts, 22f, FontStyles.Bold, UiTheme.Ui(UiTheme.Text));

            // Lay out immediately so the label never shows a frame at default (100x100) sizes.
            dotHolder.sizeDelta = new Vector2(16f, 16f);
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
