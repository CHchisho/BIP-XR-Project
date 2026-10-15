using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace SmartHouse
{
    /// <summary>
    /// Whether a real headset is in use. XR Management starts the XR runtime (OpenXR via Quest Link,
    /// or on the Quest itself) before the first scene loads, but the headset's display only starts
    /// running a few frames later, once the session is ready. Checking only the display at Start
    /// therefore wrongly reported "no headset" over Link.
    /// </summary>
    public static class XrPresence
    {
        public static bool HeadsetActive()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null) return true;

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
                if (display.running) return true;
            return false;
        }
    }
}
