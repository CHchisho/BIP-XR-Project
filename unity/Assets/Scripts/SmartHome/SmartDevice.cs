using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SmartHouse
{
    /// <summary>
    /// Base class for every interactive device in the house. Makes the object selectable
    /// with XR controllers (ray or direct) and exposes a status line for the hub panel.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public abstract class SmartDevice : MonoBehaviour
    {
        [SerializeField] string displayName = "Device";

        public string DisplayName => displayName;
        public abstract string Status { get; }

        /// <summary>Whether the device is in its "on / open / playing" state (lights the status dot).</summary>
        public virtual bool IsActive => false;
        public virtual Color AccentColor => new Color(1f, 0.78f, 0.36f);

        public event System.Action<SmartDevice> StateChanged;

        XRSimpleInteractable interactable;

        protected virtual void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            EnsureCollider();
        }

        protected virtual void OnEnable() => interactable.selectEntered.AddListener(OnSelected);
        protected virtual void OnDisable() => interactable.selectEntered.RemoveListener(OnSelected);

        void OnSelected(SelectEnterEventArgs args) => Press();

        float lastPressTime = -1f;

        /// <summary>
        /// Entry point for every input source (XR controller, desktop mouse).
        /// Ignores a second press in the same instant so two sources can't toggle twice.
        /// </summary>
        public void Press()
        {
            if (Time.unscaledTime - lastPressTime < 0.25f) return;
            lastPressTime = Time.unscaledTime;
            Interact();
        }

        /// <summary>Called when the user presses the device.</summary>
        public abstract void Interact();

        protected void NotifyChanged() => StateChanged?.Invoke(this);

        void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>() != null) return;

            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // Small devices get a slightly padded box so they are easy to hit with a ray.
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(bounds.center);
            var size = transform.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.08f), Mathf.Max(Mathf.Abs(size.y), 0.08f), Mathf.Max(Mathf.Abs(size.z), 0.08f));
            interactable.colliders.Clear();
            interactable.colliders.Add(box);
        }

        protected static AudioSource CreateAudioSource(GameObject host, bool spatial)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = 1f;
            source.maxDistance = 15f;
            return source;
        }
    }
}
