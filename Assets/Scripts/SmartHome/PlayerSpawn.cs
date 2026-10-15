using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace SmartHouse
{
    /// <summary>
    /// Puts the player at the spawn point inside the house once the headset starts tracking.
    /// Where the player physically stands in their play area decides where the head ends up, and without
    /// floor data the eyes start at floor level; this undoes both, once, at the start of the session:
    /// it lifts the view to standing height if needed, turns the player to face the spawn direction and
    /// moves the rig so the head is exactly above the spawn point.
    /// Installs itself on the scene's XR Origin; only acts when a real headset is running.
    /// </summary>
    public class PlayerSpawn : MonoBehaviour
    {
        [Tooltip("Eye height used when the headset reports no floor (eyes closer to the floor than Min Eye Height).")]
        [SerializeField] float standingEyeHeight = 1.6f;
        [SerializeField] float minEyeHeight = 0.6f;
        [Tooltip("Eyes higher than this above the floor are corrected too (e.g. a floor offset applied twice).")]
        [SerializeField] float maxEyeHeight = 2.3f;
        [Tooltip("Seconds to wait after the head is tracked, so the pose is stable.")]
        [SerializeField] float settleTime = 0.3f;

        XROrigin origin;
        Vector3 spawnPosition;
        Vector3 spawnForward;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.GetComponent<PlayerSpawn>() == null)
                xrOrigin.gameObject.AddComponent<PlayerSpawn>();
        }

        void Awake()
        {
            origin = GetComponent<XROrigin>();
            // Use the real floor from the headset's boundary, so the player stands at their own height.
            // ("Not Specified" lets the Quest pick device-relative tracking, which starts the eyes at floor level.)
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            // The rig is placed on the spawn point in the scene; remember that pose before tracking moves anything.
            spawnPosition = transform.position;
            spawnForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        }

        IEnumerator Start()
        {
            if (origin == null || origin.Camera == null) yield break;

            // Only with a real headset (the desktop simulator places the camera itself).
            if (!XrPresence.HeadsetActive()) yield break;

            // Wait until the head is really tracked. Before that the camera sits at the rig's base,
            // which reads as "eyes at floor level" and once made us lift the view onto the roof.
            while (!HeadTracked()) yield return null;
            yield return new WaitForSecondsRealtime(settleTime);

            Place();
        }

        /// <summary>Moves the player to the spawn point; can also be called later, e.g. from a "reset position" button.</summary>
        public void Place()
        {
            var head = origin.Camera.transform;

            // 1. Height: normally the headset's floor gives the real eye height. Only when that is clearly
            //    wrong (no floor data: eyes at floor level; or an offset applied twice) set standing height.
            float eyeHeight = head.position.y - transform.position.y;
            if ((eyeHeight < minEyeHeight || eyeHeight > maxEyeHeight) && origin.CameraFloorOffsetObject != null)
            {
                var offset = origin.CameraFloorOffsetObject.transform;
                offset.localPosition += Vector3.up * (standingEyeHeight - eyeHeight);
                Debug.Log($"[PlayerSpawn] Headset reported eyes {eyeHeight:0.00} m above the floor; set to {standingEyeHeight:0.00} m.");
            }

            // 2. Face the spawn direction, whatever way the player is physically looking.
            origin.MatchOriginUpCameraForward(Vector3.up, spawnForward);

            // 3. Put the head right above the spawn point, keeping its height.
            origin.MoveCameraToWorldLocation(new Vector3(spawnPosition.x, head.position.y, spawnPosition.z));
        }

        bool HeadTracked()
        {
            var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return headDevice.isValid
                && headDevice.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked
                && origin.Camera.transform.localPosition.sqrMagnitude > 0.0001f;
        }
    }
}
