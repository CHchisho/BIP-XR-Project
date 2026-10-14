using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartHouse
{
    /// <summary>
    /// Pop-up notifications that float in front of the user whenever a device changes state
    /// ("Kitchen Lamp · On"), whether it was changed in VR or in Home Assistant.
    /// The pop-ups lazily follow the head, stack up to a few at a time and fade out after a moment.
    /// </summary>
    public class StatePopups : MonoBehaviour
    {
        [SerializeField] float distance = 1f;
        [Tooltip("Degrees below eye level.")]
        [SerializeField] float pitchDown = 7f;
        [SerializeField] float followSpeed = 4f;
        [SerializeField] float showSeconds = 3f;
        [SerializeField] int maxVisible = 3;
        [Tooltip("No pop-ups right after start, while Home Assistant reports the initial states.")]
        [SerializeField] float quietAfterStart = 5f;

        class Popup
        {
            public SmartDevice Device;
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Accent;
            public TMP_Text State;
            public float Age;
        }

        readonly List<Popup> popups = new List<Popup>();
        RectTransform stack;
        bool placed;
        Material overlayImage;
        Material overlayText;

        void Start()
        {
            Build();
            foreach (var device in FindObjectsByType<SmartDevice>())
                device.StateChanged += OnStateChanged;
        }

        void OnStateChanged(SmartDevice device)
        {
            if (!device.ShowPopups || Time.timeSinceLevelLoad < quietAfterStart) return;

            var popup = popups.Find(p => p.Device == device);
            if (popup == null)
            {
                if (popups.Count >= maxVisible) Remove(popups[0]);
                popup = Create(device);
                popups.Add(popup);
            }
            popup.Age = 0f; // the same device changing again refreshes its pop-up
            Refresh(popup);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Lazy follow: drift towards a spot in front of and slightly below the eyes.
            var head = cam.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = head.up;
            forward.Normalize();
            var target = head.position + Quaternion.AngleAxis(pitchDown, Vector3.Cross(Vector3.up, forward)) * forward * distance;
            float t = placed ? 1f - Mathf.Exp(-followSpeed * Time.deltaTime) : 1f;
            placed = true;
            transform.position = Vector3.Lerp(transform.position, target, t);
            transform.rotation = Quaternion.LookRotation(transform.position - head.position);

            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var popup = popups[i];
                popup.Age += Time.deltaTime;
                Refresh(popup); // keeps live text such as "Opening 40%" current
                float fadeIn = Mathf.Clamp01(popup.Age / 0.15f);
                float fadeOut = Mathf.Clamp01((showSeconds - popup.Age) / 0.4f);
                popup.Group.alpha = Mathf.Min(fadeIn, fadeOut);
                popup.Root.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, fadeIn);
                if (popup.Age >= showSeconds) Remove(popup);
            }
        }

        static void Refresh(Popup popup)
        {
            popup.State.text = popup.Device.Status;
            var accent = popup.Device.IsActive ? popup.Device.AccentColor : UiTheme.InactiveDot;
            popup.Accent.color = UiTheme.Ui(accent);
        }

        void Remove(Popup popup)
        {
            popups.Remove(popup);
            Destroy(popup.Root.gameObject);
        }

        /// <summary>Copy of a UI material that ignores depth, so pop-ups stay visible in front of nearby walls.</summary>
        static Material OnTop(Material source)
        {
            var material = new Material(source);
            material.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = 4000;
            return material;
        }

        void Build()
        {
            overlayImage = OnTop(Canvas.GetDefaultCanvasMaterial());

            var canvasGo = new GameObject("Popups Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(500f, 400f);
            canvasRect.localScale = Vector3.one * 0.001f; // 1 unit = 1 mm

            stack = NewRect("Stack", canvasRect);
            stack.anchorMin = stack.anchorMax = new Vector2(0.5f, 0.5f);
            stack.pivot = new Vector2(0.5f, 1f);
            var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = stack.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        Popup Create(SmartDevice device)
        {
            var root = NewRect("Popup - " + device.DisplayName, stack);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = UiTheme.Ui(UiTheme.Panel);
            bg.material = overlayImage;
            var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 32, 14, 16);
            row.spacing = 16f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var size = root.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 340f;

            var accent = NewRect("Accent", root).gameObject.AddComponent<Image>();
            accent.sprite = UiSprites.Rounded;
            accent.type = Image.Type.Sliced;
            accent.material = overlayImage;
            var accentSize = accent.gameObject.AddComponent<LayoutElement>();
            accentSize.preferredWidth = 10f;
            accentSize.preferredHeight = 68f;

            var texts = NewRect("Texts", root);
            var column = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 0f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = column.childForceExpandHeight = false;

            var title = NewText(texts, device.DisplayName.ToUpperInvariant(), 18f, UiTheme.TextDim);
            title.characterSpacing = 3f;
            var state = NewText(texts, device.Status, 34f, UiTheme.Text);

            var popup = new Popup { Device = device, Root = root, Group = group, Accent = accent, State = state };
            LayoutRebuilder.ForceRebuildLayoutImmediate(stack);
            return popup;
        }

        TMP_Text NewText(Transform parent, string value, float size, Color color)
        {
            var text = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (overlayText == null) overlayText = OnTop(text.fontSharedMaterial);
            text.fontSharedMaterial = overlayText;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = UiTheme.Ui(color);
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
    }
}
