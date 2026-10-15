using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartHouse
{
    /// <summary>
    /// Wall-mounted dashboard listing every device with a status dot and its state,
    /// in the same style as the floating status labels.
    /// </summary>
    public class SmartHomeHub : MonoBehaviour
    {
        [SerializeField] Vector2 sizeMm = new Vector2(420f, 330f);

        class Row
        {
            public SmartDevice Device;
            public Image Dot;
            public TMP_Text State;
        }

        readonly List<Row> rows = new List<Row>();

        void Start()
        {
            var devices = FindObjectsByType<SmartDevice>();
            System.Array.Sort(devices, (a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            Build(devices);
            foreach (var device in devices) device.StateChanged += _ => Refresh();
            Refresh();
        }

        void Update()
        {
            if (Time.frameCount % 10 == 0) Refresh();
        }

        void Refresh()
        {
            foreach (var row in rows)
            {
                row.State.text = row.Device.Status;
                row.Dot.color = UiTheme.Ui(row.Device.IsActive ? row.Device.AccentColor : UiTheme.InactiveDot);
                row.State.color = UiTheme.Ui(row.Device.IsActive ? UiTheme.Text : UiTheme.TextDim);
            }
        }

        void Build(SmartDevice[] devices)
        {
            var canvasGo = new GameObject("Dashboard", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvasGo.transform;
            root.sizeDelta = sizeMm;
            root.localScale = Vector3.one * 0.001f;

            var bg = canvasGo.AddComponent<Image>();
            bg.sprite = UiSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = UiTheme.Ui(UiTheme.Panel);

            var list = canvasGo.AddComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset(26, 26, 22, 22);
            list.spacing = 8f;
            list.childControlWidth = true;
            list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;

            var header = Text(root, "SMART HOUSE", 13f, FontStyles.Bold, UiTheme.Ui(UiTheme.TextDim));
            header.characterSpacing = 12f;
            var subtitle = Text(root, "Point at a device and pull the trigger", 12f, FontStyles.Normal, UiTheme.Ui(UiTheme.TextDim));
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            foreach (var device in devices)
            {
                var rowRect = NewRect("Row", root);
                rowRect.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
                var line = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
                line.spacing = 12f;
                line.childAlignment = TextAnchor.MiddleLeft;
                line.childControlWidth = true;
                line.childControlHeight = true;
                line.childForceExpandWidth = false;
                line.childForceExpandHeight = false;

                var dot = NewRect("Dot", rowRect).gameObject.AddComponent<Image>();
                dot.sprite = UiSprites.Circle;
                var dotLayout = dot.gameObject.AddComponent<LayoutElement>();
                dotLayout.preferredWidth = dotLayout.preferredHeight = 12f;

                var name = Text(rowRect, device.DisplayName, 16f, FontStyles.Normal, UiTheme.Ui(UiTheme.Text));
                name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var state = Text(rowRect, "", 16f, FontStyles.Bold, Color.white);
                state.alignment = TextAlignmentOptions.MidlineRight;

                rows.Add(new Row { Device = device, Dot = dot, State = state });
            }
        }

        static TMP_Text Text(Transform parent, string value, float size, FontStyles style, Color color)
        {
            var text = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
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
