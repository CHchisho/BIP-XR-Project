using UnityEngine;
using UnityEngine.InputSystem;

namespace SmartHouse
{
    /// <summary>
    /// The trigger on the Quest controllers, aiming along the visible ray:
    /// - pull and release: press the device (or its wall panel) you point at, like the B / Y button;
    /// - hold and twist the wrist like turning a knob: adjust it (clockwise = brighter / louder / warmer).
    /// XRI selects with the grip, so the trigger is read here directly. The device stays locked while
    /// the trigger is held, so the ray may drift off it during the twist.
    /// Installs itself on the scene's XR Origin; does nothing without a headset (the mouse is used then).
    /// </summary>
    public class TriggerTwist : MonoBehaviour
    {
        [SerializeField] float maxDistance = 30f;

        class Hand
        {
            public Transform Aim;
            public InputAction Trigger;
            public SmartDevice Device;
            public Quaternion LastRotation;
            public float Twist;
            public bool Twisting;
        }

        Hand left;
        Hand right;
        bool? headset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.GetComponent<TriggerTwist>() == null)
                xrOrigin.gameObject.AddComponent<TriggerTwist>();
        }

        void Awake()
        {
            left = NewHand("Left Controller", "LeftHand");
            right = NewHand("Right Controller", "RightHand");
        }

        Hand NewHand(string controllerName, string usage)
        {
            var hand = new Hand
            {
                Aim = FindAim(controllerName),
                Trigger = new InputAction($"Twist ({usage})", InputActionType.Button, $"<XRController>{{{usage}}}/{{TriggerButton}}"),
            };
            hand.Trigger.started += _ => Begin(hand);
            hand.Trigger.canceled += _ => End(hand);
            return hand;
        }

        void OnEnable() { left.Trigger.Enable(); right.Trigger.Enable(); }

        void OnDisable()
        {
            left.Trigger.Disable();
            right.Trigger.Disable();
            left.Device = right.Device = null;
        }

        void OnDestroy() { left.Trigger.Dispose(); right.Trigger.Dispose(); }

        /// <summary>Same aim as <see cref="PointTeleport"/>: the near-far interactor the ray starts from.</summary>
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

        void Begin(Hand hand)
        {
            headset ??= XrPresence.HeadsetActive();
            if (headset != true || hand.Aim == null) return;
            if (!Physics.Raycast(hand.Aim.position, hand.Aim.forward, out var hit, maxDistance, ~0, QueryTriggerInteraction.Ignore)) return;
            var panel = hit.collider.GetComponentInParent<StatePanel>();
            hand.Device = panel != null ? panel.Device : hit.collider.GetComponentInParent<SmartDevice>();
            hand.LastRotation = hand.Aim.rotation;
            hand.Twist = 0f;
            hand.Twisting = false;
        }

        void End(Hand hand)
        {
            var device = hand.Device;
            hand.Device = null;
            if (device != null && !hand.Twisting) device.Press();
        }

        void Update()
        {
            Track(left);
            Track(right);
        }

        void Track(Hand hand)
        {
            if (hand.Device == null || hand.Aim == null) return;
            if (!(hand.Device is IAdjustable adjustable)) return;

            hand.Twist += WristTwist.Delta(hand.LastRotation, hand.Aim.rotation);
            hand.LastRotation = hand.Aim.rotation;
            if (!hand.Twisting)
            {
                if (Mathf.Abs(hand.Twist) <= WristTwist.DeadZoneDegrees) return;
                hand.Twisting = true;
                hand.Twist = Mathf.Sign(hand.Twist) * (Mathf.Abs(hand.Twist) - WristTwist.DeadZoneDegrees);
            }
            if (hand.Twist == 0f) return;
            adjustable.Adjust(hand.Twist / WristTwist.FullRangeDegrees);
            hand.Twist = 0f;
        }
    }
}
