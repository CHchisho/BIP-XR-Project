using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Makes each controller's pointer ray start from the front tip of the controller model you see,
    /// centred on it left-to-right and up-and-down.
    /// Both the model ("Controller Visual") and the ray (Near-Far Interactor) are children of the controller,
    /// which follows the tracked aim pose; that pose stays authoritative and the ray start is set in its local space.
    /// The models only appear once the controllers are tracked, so this keeps checking until each one
    /// is visible, then measures it. If a model never shows up within <see cref="measureTimeout"/>, the ray
    /// starts at the aim point plus <see cref="fallbackOffset"/>. Every field can be changed live in Play mode
    /// (on the XR Origin): the ray start is re-applied each frame and each change is logged.
    /// Installs itself on the scene's XR Origin at Play, so edit it in Play mode.
    /// </summary>
    public class ControllerRayAnchor : MonoBehaviour
    {
        static readonly string[] Controllers = { "Left Controller", "Right Controller" };

        [Tooltip("Extra shift added in both modes, in metres in controller space (x = right, y = up, z = forward).")]
        [SerializeField] Vector3 extraOffset = Vector3.zero;
        [Tooltip("Only when the model could not be measured: ray start relative to the tracked aim point, in metres.")]
        [SerializeField] Vector3 fallbackOffset = new Vector3(0.02f, 0f, 0f);
        [Tooltip("Seconds to wait for the controller models before switching to the fallback.")]
        [SerializeField] float measureTimeout = 20f;

        class Hand
        {
            public string Name;
            public Transform Controller;
            public Transform Visual;
            public Transform Ray;
            public Vector3 AimPoint;  // where XRI put the ray, in the controller's local space (the tracked aim pose)
            public Vector3 ModelTip;  // front-centre of the visible model, same space
            public Mode Mode;
        }

        enum Mode { Waiting, Measured, Fallback }

        readonly Hand[] hands = new Hand[2];
        Vector3 loggedExtra, loggedFallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.GetComponent<ControllerRayAnchor>() == null)
                xrOrigin.gameObject.AddComponent<ControllerRayAnchor>();
        }

        IEnumerator Start()
        {
            for (int i = 0; i < Controllers.Length; i++) hands[i] = Find(Controllers[i]);
            loggedExtra = extraOffset;
            loggedFallback = fallbackOffset;

            float started = Time.realtimeSinceStartup;
            var wait = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                bool pending = false;
                foreach (var hand in hands)
                {
                    if (hand == null || hand.Mode != Mode.Waiting) continue;
                    if (TryMeasure(hand, out var tip))
                    {
                        hand.ModelTip = tip;
                        hand.Mode = Mode.Measured;
                        Debug.Log($"[ControllerRayAnchor] {hand.Name}: ray now starts at the model tip, {(tip - hand.AimPoint) * 100f:F1} cm from the aim point (x right, y up, z forward).");
                    }
                    else pending = true;
                }
                if (!pending) yield break;
                if (Time.realtimeSinceStartup - started > measureTimeout) // read live, so it can be changed in Play mode
                {
                    foreach (var hand in hands)
                    {
                        if (hand == null || hand.Mode != Mode.Waiting) continue;
                        hand.Mode = Mode.Fallback;
                        Debug.LogWarning($"[ControllerRayAnchor] {hand.Name}: model never became visible; using Fallback Offset {fallbackOffset * 100f:F1} cm from the aim point.");
                    }
                    yield break;
                }
                yield return wait;
            }
        }

        // Every frame, after XRI and the tracked pose driver have run, so Inspector edits show at once
        // and nothing can leave the ray somewhere else.
        void LateUpdate()
        {
            foreach (var hand in hands)
                if (hand != null && hand.Mode != Mode.Waiting) Apply(hand);

            if (extraOffset != loggedExtra || fallbackOffset != loggedFallback)
            {
                loggedExtra = extraOffset;
                loggedFallback = fallbackOffset;
                foreach (var hand in hands)
                    if (hand != null)
                        Debug.Log($"[ControllerRayAnchor] {hand.Name} ({hand.Mode}): ray start now {RayStart(hand) * 100f:F1} cm in controller space.");
            }
        }

        Vector3 RayStart(Hand hand) => hand.Mode switch
        {
            Mode.Measured => hand.ModelTip + extraOffset,
            Mode.Fallback => hand.AimPoint + fallbackOffset + extraOffset,
            _ => hand.AimPoint,
        };

        void Apply(Hand hand) => hand.Ray.position = hand.Controller.TransformPoint(RayStart(hand));

        /// <summary>Front-centre of the visible model, in the controller's local space (+z = pointing direction).</summary>
        static bool TryMeasure(Hand hand, out Vector3 tip)
        {
            tip = default;
            if (hand.Visual == null) return false;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            bool any = false;
            foreach (var r in hand.Visual.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.localBounds;
                if (b.size.sqrMagnitude < 1e-6f) continue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = hand.Controller.InverseTransformPoint(r.transform.TransformPoint(corner));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                any = true;
            }
            if (!any || (max - min).magnitude < 0.03f) return false; // not a real controller model (yet)
            tip = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, max.z);
            return true;
        }

        Hand Find(string controllerName)
        {
            foreach (var controller in GetComponentsInChildren<Transform>(true))
            {
                if (controller.name != controllerName) continue;
                var hand = new Hand { Name = controllerName, Controller = controller };
                foreach (var t in controller.GetComponentsInChildren<Transform>(true))
                {
                    if (hand.Visual == null && t.name.Contains("Controller Visual")) hand.Visual = t;
                    if (hand.Ray == null && (t.name.Contains("NearFar") || t.name.Contains("Near-Far"))) hand.Ray = t;
                }
                if (hand.Ray == null)
                {
                    Debug.LogWarning($"[ControllerRayAnchor] {controllerName}: no Near-Far Interactor found; ray left as is.");
                    return null;
                }
                if (hand.Visual == null)
                    Debug.LogWarning($"[ControllerRayAnchor] {controllerName}: no controller model found; Fallback Offset will be used.");
                hand.AimPoint = controller.InverseTransformPoint(hand.Ray.position);
                return hand;
            }
            return null;
        }
    }
}
