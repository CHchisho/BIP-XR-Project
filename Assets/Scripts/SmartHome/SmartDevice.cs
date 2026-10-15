using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SmartHouse
{
    /// <summary>
    /// Base class for every interactive device in the house. Makes the object selectable
    /// with XR controllers (ray or direct) and exposes a status line for the hub panel.
    /// Devices that implement <see cref="IAdjustable"/> can also be dragged: hold the trigger and
    /// move the controller up or down to change the value; a short press without moving still presses.
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

        /// <summary>False for devices that only mirror another device (a wall switch shows its lamp's state).</summary>
        public virtual bool ShowPopups => true;

        /// <summary>The device this one operates (a wall switch's lamp), or null. Its wall panel lights up on hover too.</summary>
        public virtual SmartDevice ControlledDevice => null;

        public event System.Action<SmartDevice> StateChanged;

        XRSimpleInteractable interactable;

        protected virtual void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            EnsureCollider();
            if (GetComponent<DeviceHoverFeedback>() == null) gameObject.AddComponent<DeviceHoverFeedback>();
        }

        protected virtual void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
            interactable.selectExited.AddListener(OnDeselected);
        }

        protected virtual void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.selectExited.RemoveListener(OnDeselected);
            dragInteractor = null;
        }

        // Controller movement (metres, up/down) that changes an adjustable device over its full range.
        const float DragFullRange = 0.3f;
        const float DragDeadZone = 0.025f;

        Transform dragInteractor;
        bool dragged;
        float selectTime;

        void OnSelected(SelectEnterEventArgs args)
        {
            if (this is IAdjustable)
            {
                // Decide on release: a press if the hand stayed still, otherwise it was a drag.
                dragInteractor = args.interactorObject.transform;
                dragged = false;
                selectTime = Time.unscaledTime;
                StartCoroutine(Drag(dragInteractor));
                return;
            }
            Press();
        }

        void OnDeselected(SelectExitEventArgs args)
        {
            if (dragInteractor == null || args.interactorObject.transform != dragInteractor) return;
            dragInteractor = null;
            // In the desktop simulator one mouse click is both a DesktopPointer press and an XR select;
            // if the device was already pressed during this select, don't toggle it a second time.
            if (!dragged && lastPressTime < selectTime - 0.05f) Press();
        }

        IEnumerator Drag(Transform hand)
        {
            float startY = hand.position.y;
            float lastY = startY;
            while (dragInteractor == hand && hand != null)
            {
                float y = hand.position.y;
                if (!dragged && Mathf.Abs(y - startY) > DragDeadZone)
                {
                    dragged = true;
                    lastY = startY + Mathf.Sign(y - startY) * DragDeadZone;
                }
                if (dragged && !Mathf.Approximately(y, lastY))
                {
                    ((IAdjustable)this).Adjust((y - lastY) / DragFullRange);
                    lastY = y;
                }
                yield return null;
            }
        }

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
