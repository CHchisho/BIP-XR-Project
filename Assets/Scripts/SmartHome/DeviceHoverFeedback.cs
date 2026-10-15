using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SmartHouse
{
    /// <summary>
    /// Shows that a device can be used: while a controller ray (or the desktop mouse) points at it,
    /// the device gets a soft blue rim glow, and the controller gives one short, light vibration.
    /// Added to every SmartDevice automatically.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class DeviceHoverFeedback : MonoBehaviour
    {
        const float HapticAmplitude = 0.25f;
        const float HapticDuration = 0.04f;

        static Material rimMaterial;

        readonly HashSet<object> sources = new HashSet<object>();
        readonly List<GameObject> shells = new List<GameObject>();
        XRSimpleInteractable interactable;
        SmartDevice device;
        bool built;

        /// <summary>True while anything points at the device (or at its wall panel).</summary>
        public bool Highlighted => sources.Count > 0;

        /// <summary>Raised when <see cref="Highlighted"/> changes, so the device's wall panel can glow along.</summary>
        public event System.Action<bool> HighlightChanged;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            device = GetComponent<SmartDevice>();
        }

        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            sources.Clear();
            Show(false);
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            // Only controllers (they carry a haptics player); the gaze and teleport helpers don't count.
            if (!Buzz(args.interactorObject)) return;
            SetHighlighted(args.interactorObject, true);
        }

        void OnHoverExited(HoverExitEventArgs args) => SetHighlighted(args.interactorObject, false);

        /// <summary>Highlight on/off for one source (a controller, the mouse); glows while any source points at it.</summary>
        public void SetHighlighted(object source, bool on)
        {
            bool was = Highlighted;
            if (on) sources.Add(source);
            else sources.Remove(source);
            Show(Highlighted);
            if (was != Highlighted) HighlightChanged?.Invoke(Highlighted);
        }

        /// <summary>One short, light vibration on the controller behind an interactor (none for gaze/teleport).</summary>
        public static bool Buzz(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor)
        {
            var haptics = interactor.transform.GetComponentInParent<HapticImpulsePlayer>();
            if (haptics == null) return false;
            haptics.SendHapticImpulse(HapticAmplitude, HapticDuration);
            return true;
        }

        void Show(bool on)
        {
            if (on && !built) Build();
            foreach (var shell in shells)
                if (shell != null) shell.SetActive(on);
        }

        /// <summary>A glow copy of each of the device's own meshes (not those of devices mounted on it).</summary>
        void Build()
        {
            built = true;
            if (rimMaterial == null)
            {
                var shader = Shader.Find("SmartHouse/HoverRim");
                if (shader == null) return;
                rimMaterial = new Material(shader) { name = "Hover Rim (runtime)" };
            }

            foreach (var source in GetComponentsInChildren<MeshRenderer>())
            {
                if (!source.enabled) continue; // hidden helper meshes must not glow
                if (source.GetComponentInParent<SmartDevice>() != device) continue;
                if (source.GetComponentInParent<Canvas>() != null) continue;
                var filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                var shell = new GameObject("Hover Rim");
                shell.transform.SetParent(source.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = shell.AddComponent<MeshRenderer>();
                var materials = new Material[filter.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = rimMaterial;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                shell.SetActive(false);
                shells.Add(shell);
            }
        }
    }
}
