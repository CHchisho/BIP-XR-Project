using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SmartHouse
{
    /// <summary>
    /// Lets you use the devices with the mouse when no headset is running:
    /// left-click a device in the Game view to press it. A label shows which device is under the cursor.
    /// </summary>
    public class DesktopPointer : MonoBehaviour
    {
        [SerializeField] float maxDistance = 30f;

        bool active;
        SmartDevice hovered;
        GUIStyle labelStyle;

        void Start()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            active = true;
            foreach (var display in displays)
                if (display.running) active = false;
        }

        void Update()
        {
            hovered = null;
            if (!active || Mouse.current == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, maxDistance, ~0, QueryTriggerInteraction.Collide))
                hovered = hit.collider.GetComponentInParent<SmartDevice>();

            if (hovered != null && Mouse.current.leftButton.wasPressedThisFrame)
                hovered.Press();
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
                var text = $"{hovered.DisplayName}: {hovered.Status}  (click)";
                GUI.Box(new Rect(mouse.x + 18f, Screen.height - mouse.y + 6f, 360f, 28f), text, labelStyle);
            }
        }
    }
}
