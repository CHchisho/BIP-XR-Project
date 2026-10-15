using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SmartHouse
{
    /// <summary>
    /// Drives the Meta Quest 3 (Touch Plus) controller model from the WebXR input-profiles asset set.
    /// The model is authored in grip space, but the rig's controller object follows the aim (pointer) pose,
    /// so the model is moved to where the grip pose sits relative to the aim pose each frame.
    /// Buttons, trigger, grip and thumbstick are animated the WebXR way: every moving part has a
    /// "_value" node that is blended between its "_min" and "_max" sibling nodes.
    /// </summary>
    public class MetaQuestControllerVisual : MonoBehaviour
    {
        [SerializeField] bool leftHand = true;
        [Tooltip("The model's root (in grip space). Defaults to the first child.")]
        [SerializeField] Transform model;
        [Tooltip("Grip pose relative to the aim pose, used until the device reports both (e.g. in the simulator).")]
        [SerializeField] Vector3 fallbackGripPosition = new Vector3(0f, -0.045f, -0.025f);
        [SerializeField] Vector3 fallbackGripEuler = new Vector3(-50f, 0f, 0f);   // handle tilts back towards the wrist

        class Part
        {
            public Transform Value, Min, Max;
            public void Set(float t)
            {
                Value.localPosition = Vector3.Lerp(Min.localPosition, Max.localPosition, t);
                Value.localRotation = Quaternion.Slerp(Min.localRotation, Max.localRotation, t);
            }
        }

        readonly List<(Part part, InputAction action)> buttons = new List<(Part, InputAction)>();
        Part stickX, stickY;
        InputAction stick, devicePos, deviceRot, pointerPos, pointerRot;
        readonly List<InputAction> actions = new List<InputAction>();

        void Awake()
        {
            if (model == null && transform.childCount > 0) model = transform.GetChild(0);
            string hand = leftHand ? "{LeftHand}" : "{RightHand}";

            AddButton("xr_standard_trigger", $"<XRController>{hand}/trigger");
            AddButton("xr_standard_squeeze", $"<XRController>{hand}/grip");
            AddButton("xr_standard_thumbstick", $"<XRController>{hand}/thumbstickClicked");
            AddButton(leftHand ? "x_button" : "a_button", $"<XRController>{hand}/primaryButton");
            AddButton(leftHand ? "y_button" : "b_button", $"<XRController>{hand}/secondaryButton");
            stickX = FindPart("xr_standard_thumbstick_xaxis");
            stickY = FindPart("xr_standard_thumbstick_yaxis");
            stick = Track(new InputAction(type: InputActionType.Value, binding: $"<XRController>{hand}/thumbstick"));
            devicePos = Track(new InputAction(type: InputActionType.Value, binding: $"<XRController>{hand}/devicePosition"));
            deviceRot = Track(new InputAction(type: InputActionType.Value, binding: $"<XRController>{hand}/deviceRotation"));
            pointerPos = Track(new InputAction(type: InputActionType.Value, binding: $"<XRController>{hand}/pointerPosition"));
            pointerRot = Track(new InputAction(type: InputActionType.Value, binding: $"<XRController>{hand}/pointerRotation"));
        }

        void OnEnable() { foreach (var a in actions) a.Enable(); }
        void OnDisable() { foreach (var a in actions) a.Disable(); }
        void OnDestroy() { foreach (var a in actions) a.Dispose(); }

        InputAction Track(InputAction action) { actions.Add(action); return action; }

        void AddButton(string node, string binding)
        {
            var part = FindPart(node);
            if (part != null) buttons.Add((part, Track(new InputAction(type: InputActionType.Value, binding: binding))));
        }

        Part FindPart(string node)
        {
            Transform value = null, min = null, max = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == node + "_pressed_value") value = t;
                else if (t.name == node + "_pressed_min") min = t;
                else if (t.name == node + "_pressed_max") max = t;
            }
            return value != null && min != null && max != null ? new Part { Value = value, Min = min, Max = max } : null;
        }

        void LateUpdate()
        {
            foreach (var (part, action) in buttons)
                part.Set(Mathf.Clamp01(action.ReadValue<float>()));

            var s = stick.ReadValue<Vector2>();
            stickX?.Set((s.x + 1f) * 0.5f);
            stickY?.Set((-s.y + 1f) * 0.5f);   // WebXR's y axis points down (+y = pulled back)

            if (model == null) return;
            var aimRot = pointerRot.ReadValue<Quaternion>();
            var gripRot = deviceRot.ReadValue<Quaternion>();
            bool havePoses = aimRot != default && gripRot != default
                             && (pointerPos.ReadValue<Vector3>() != devicePos.ReadValue<Vector3>() || aimRot != gripRot);
            if (havePoses)
            {
                var inv = Quaternion.Inverse(aimRot);
                model.localPosition = inv * (devicePos.ReadValue<Vector3>() - pointerPos.ReadValue<Vector3>());
                model.localRotation = inv * gripRot;
            }
            else
            {
                model.localPosition = fallbackGripPosition;
                model.localRotation = Quaternion.Euler(fallbackGripEuler);
            }
        }
    }
}
