using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SmartHouse
{
    /// <summary>
    /// A framed panel hung on the wall like a painting, showing one device's state.
    /// It is placed in the scene and never moves, turns or scales.
    /// Pointing at it lights its frame, makes the device glow and gives one short vibration;
    /// pointing at the device, or at the switch that operates it, lights the panel too.
    /// Pulling the trigger on the panel works the device, like pressing the device itself.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class StatePanel : MonoBehaviour
    {
        [SerializeField] SmartDevice device;
        [SerializeField] Vector2 sizeMeters = new Vector2(0.30f, 0.17f);
        [Tooltip("The border around the board; it glows while the panel or its device is pointed at.")]
        [SerializeField] Renderer frame;
        [SerializeField] Color glowColor = new Color(0.35f, 0.75f, 1f);

        static readonly Color FrameIdle = new Color(0.16f, 0.18f, 0.22f);

        readonly List<DeviceHoverFeedback> watched = new List<DeviceHoverFeedback>();
        XRSimpleInteractable interactable;
        DeviceHoverFeedback deviceFeedback;
        Material frameMaterial;
        TMP_Text title;
        TMP_Text state;
        Image dot;

        public SmartDevice Device => device;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            // No light probes: Unity 6's GPU Resident Drawer fails while interpolating probes for objects like
            // these (GetActiveBuildProfile on a worker thread) and then draws them at stale positions, so the
            // panels appeared to drift as you moved. Flat panels look the same with plain ambient light.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            if (frame != null)
            {
                frameMaterial = frame.material;
                frameMaterial.EnableKeyword("_EMISSION");
            }
            Build();
        }

        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            interactable.selectEntered.AddListener(OnSelected);
        }

        void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelected);
        }

        void Start()
        {
            if (device == null) return;
            deviceFeedback = device.GetComponent<DeviceHoverFeedback>();
            device.StateChanged += _ => Refresh();

            // Glow along with the device and with every switch that operates it.
            Watch(device);
            foreach (var other in FindObjectsByType<SmartDevice>())
                if (other.ControlledDevice == device) Watch(other);

            Refresh();
            ShowGlow(false);
        }

        void Watch(SmartDevice target)
        {
            var feedback = target.GetComponent<DeviceHoverFeedback>();
            if (feedback == null) return;
            watched.Add(feedback);
            feedback.HighlightChanged += _ => UpdateGlow();
        }

        void Update()
        {
            // Animated states (a curtain moving, a lock turning) change text only; the panel itself stays put.
            if (Time.frameCount % 10 == 0) Refresh();
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!DeviceHoverFeedback.Buzz(args.interactorObject)) return; // controllers only, not gaze/teleport
            if (deviceFeedback != null) deviceFeedback.SetHighlighted((this, args.interactorObject), true);
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            if (deviceFeedback != null) deviceFeedback.SetHighlighted((this, args.interactorObject), false);
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            if (device != null) device.Press();
        }

        void UpdateGlow()
        {
            bool on = false;
            foreach (var feedback in watched)
                if (feedback != null && feedback.Highlighted) on = true;
            ShowGlow(on);
        }

        void ShowGlow(bool on)
        {
            if (frameMaterial == null) return;
            frameMaterial.SetColor("_BaseColor", on ? glowColor : FrameIdle);
            frameMaterial.SetColor("_EmissionColor", on ? glowColor * 2.5f : Color.black);
        }

        void Refresh()
        {
            if (device == null || state == null) return;
            state.text = device.Status;
            dot.color = UiTheme.Ui(device.IsActive ? device.AccentColor : UiTheme.InactiveDot);
        }

        /// <summary>The text on the board: a world-space canvas just in front of it, 1 canvas unit = 1 mm.</summary>
        void Build()
        {
            float w = sizeMeters.x * 1000f, h = sizeMeters.y * 1000f;
            float k = h / 170f; // text scales with the panel height
            float pad = 14f * k;

            var canvasGo = new GameObject("Display", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, -0.0115f); // 1.5 mm in front of the board's face (board front at -10 mm)
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvasGo.transform;
            root.sizeDelta = new Vector2(w, h);
            root.localScale = Vector3.one * 0.001f;

            dot = NewRect("Dot", root, new Vector2(0f, 1f), new Vector2(pad, -pad - 2f * k), new Vector2(14f * k, 14f * k)).gameObject.AddComponent<Image>();
            dot.sprite = UiSprites.Circle;
            dot.raycastTarget = false;

            title = NewText(root, "Title", (device != null ? device.DisplayName : "Device").ToUpperInvariant(), 15f * k, FontStyles.Bold, UiTheme.TextDim,
                new Vector2(pad + 24f * k, -pad), new Vector2(w - 2f * pad - 24f * k, 22f * k));
            title.characterSpacing = 4f;

            state = NewText(root, "State", "", 40f * k, FontStyles.Bold, UiTheme.Text,
                new Vector2(pad, -pad - 34f * k), new Vector2(w - 2f * pad, 58f * k));
            state.enableAutoSizing = true;
            state.fontSizeMin = 18f * k;
            state.fontSizeMax = 40f * k;

            string hint = device is IAdjustable ? "Trigger: switch  ·  hold + move hand: adjust" : "Point and pull the trigger";
            NewText(root, "Hint", hint, 11f * k, FontStyles.Normal, UiTheme.TextDim,
                new Vector2(pad, -(h - pad - 16f * k)), new Vector2(w - 2f * pad, 16f * k));
        }

        static RectTransform NewRect(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static TMP_Text NewText(RectTransform parent, string name, string value, float size, FontStyles style, Color color, Vector2 position, Vector2 box)
        {
            var text = NewRect(name, parent, new Vector2(0f, 1f), position, box).gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = UiTheme.Ui(color);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate; // LiberationSans SDF has no "…" glyph; Ellipsis only logged warnings
            text.raycastTarget = false;
            return text;
        }
    }
}
