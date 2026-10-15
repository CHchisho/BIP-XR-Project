using UnityEngine;
using UnityEngine.InputSystem;

namespace SmartHouse
{
    /// <summary>
    /// Lets you use the devices with the mouse when no headset is running:
    /// left-click a device in the Game view to press it, scroll over a dimmer, lamp or thermostat to adjust it,
    /// shift-scroll over a color lamp to change its color.
    /// A label shows which device is under the cursor.
    /// </summary>
    public class DesktopPointer : MonoBehaviour
    {
        [SerializeField] float maxDistance = 30f;
        [Tooltip("Change per scroll-wheel notch, as a fraction of the device's full range.")]
        [SerializeField] float scrollStep = 0.05f;

        const float ColorScrollStep = 1f / 24f;

        bool active;
        SmartDevice hovered;
        GUIStyle labelStyle;

        void Start()
        {
            active = !XrPresence.HeadsetActive();
        }

        void Update()
        {
            var previous = hovered;
            hovered = null;
            if (!active || Mouse.current == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, maxDistance, ~0, QueryTriggerInteraction.Collide))
            {
                // A wall panel stands for its device: hovering or clicking it works the device.
                var panel = hit.collider.GetComponentInParent<StatePanel>();
                hovered = panel != null ? panel.Device : hit.collider.GetComponentInParent<SmartDevice>();
            }

            if (previous != hovered)
            {
                if (previous != null && previous.TryGetComponent(out DeviceHoverFeedback oldGlow)) oldGlow.SetHighlighted(this, false);
                if (hovered != null && hovered.TryGetComponent(out DeviceHoverFeedback newGlow)) newGlow.SetHighlighted(this, true);
            }

            if (hovered == null) return;
            if (Mouse.current.leftButton.wasPressedThisFrame)
                hovered.Press();

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll == 0f) return;
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (shift && hovered.ColorTarget != null)
                hovered.ColorTarget.AdjustColor(Mathf.Sign(scroll) * ColorScrollStep);
            else if (hovered is IAdjustable adjustable)
                adjustable.Adjust(Mathf.Sign(scroll) * scrollStep);
        }

        void OnGUI()
        {
            if (!active) return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
                labelStyle.normal.textColor = Color.white;
            }

            if (hovered != null)
            {
                var mouse = Mouse.current.position.ReadValue();
                var hint = hovered is IAdjustable ? "click · scroll" : "click";
                if (hovered.ColorTarget != null) hint += " · shift-scroll: color";
                var text = $"{hovered.DisplayName}: {hovered.Status}  ({hint})";
                GUI.Box(new Rect(mouse.x + 18f, Screen.height - mouse.y + 6f, 420f, 28f), text, labelStyle);
            }
        }
    }
}
