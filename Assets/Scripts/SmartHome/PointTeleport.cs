using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace SmartHouse
{
    /// <summary>
    /// Point-and-press buttons on the controllers, aiming along the visible ray:
    /// - A (right) / X (left): jump to the floor, porch or garden you point at, keeping your facing.
    ///   Walls, furniture tops and the roof are not valid targets.
    /// - B (right) / Y (left): press the device (or its wall panel) you point at, like the trigger.
    /// Installs itself on the scene's XR Origin.
    /// </summary>
    public class PointTeleport : MonoBehaviour
    {
        [SerializeField] float maxDistance = 30f;

        XROrigin origin;
        CharacterController body;
        Transform leftAim;
        Transform rightAim;
        InputAction leftButton;
        InputAction rightButton;
        InputAction leftPress;
        InputAction rightPress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.GetComponent<PointTeleport>() == null)
                xrOrigin.gameObject.AddComponent<PointTeleport>();
        }

        void Awake()
        {
            origin = GetComponent<XROrigin>();
            body = GetComponent<CharacterController>();
            leftAim = FindAim("Left Controller");
            rightAim = FindAim("Right Controller");

            leftButton = new InputAction("Teleport (left)", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            rightButton = new InputAction("Teleport (right)", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            leftButton.performed += _ => TeleportAlong(leftAim);
            rightButton.performed += _ => TeleportAlong(rightAim);

            leftPress = new InputAction("Press (left)", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
            rightPress = new InputAction("Press (right)", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            leftPress.performed += _ => PressAlong(leftAim);
            rightPress.performed += _ => PressAlong(rightAim);
        }

        void OnEnable()
        {
            leftButton.Enable();
            rightButton.Enable();
            leftPress.Enable();
            rightPress.Enable();
        }

        void OnDisable()
        {
            leftButton.Disable();
            rightButton.Disable();
            leftPress.Disable();
            rightPress.Disable();
        }

        void OnDestroy()
        {
            leftButton.Dispose();
            rightButton.Dispose();
            leftPress.Dispose();
            rightPress.Dispose();
        }

        /// <summary>The transform the controller's visible ray starts from (its near-far interactor), else the controller.</summary>
        Transform FindAim(string controllerName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != controllerName) continue;
                foreach (var child in t.GetComponentsInChildren<Transform>(true))
                    if (child.name.Contains("NearFar") || child.name.Contains("Near-Far")) return child;
                return t;
            }
            return null;
        }

        void TeleportAlong(Transform aim)
        {
            if (aim == null || origin == null || origin.Camera == null) return;
            if (!Physics.Raycast(aim.position, aim.forward, out var hit, maxDistance, ~0, QueryTriggerInteraction.Ignore)) return;
            if (!IsValidTarget(hit)) return;

            // Keep the head's height above the floor and land the feet on the hit point.
            var head = origin.Camera.transform;
            float eyeHeight = head.position.y - transform.position.y;

            bool hadBody = body != null && body.enabled;
            if (hadBody) body.enabled = false; // a CharacterController would otherwise resist the jump
            origin.MoveCameraToWorldLocation(new Vector3(hit.point.x, hit.point.y + eyeHeight, hit.point.z));
            if (hadBody) body.enabled = true;
        }

        /// <summary>Presses the device under the ray: a device itself, or the device a wall panel shows.</summary>
        void PressAlong(Transform aim)
        {
            if (aim == null) return;
            if (!Physics.Raycast(aim.position, aim.forward, out var hit, maxDistance, ~0, QueryTriggerInteraction.Ignore)) return;
            var panel = hit.collider.GetComponentInParent<StatePanel>();
            var device = panel != null ? panel.Device : hit.collider.GetComponentInParent<SmartDevice>();
            if (device != null) device.Press();
        }

        static bool IsValidTarget(RaycastHit hit)
        {
            if (hit.collider.GetComponentInParent<TeleportationArea>() != null) return true;
            // Flat ground that isn't marked, but never a table, counter or the roof.
            return hit.normal.y > 0.85f && hit.point.y < 0.3f;
        }
    }
}
