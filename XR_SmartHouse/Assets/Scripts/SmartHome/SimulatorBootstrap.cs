using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace SmartHouse
{
    /// <summary>
    /// Spawns the XR Device Simulator when no headset is running, so the app can be
    /// tested with mouse and keyboard.
    /// </summary>
    public class SimulatorBootstrap : MonoBehaviour
    {
        [SerializeField] GameObject simulatorPrefab;
        [SerializeField] bool allowInBuilds;

        void Start()
        {
            if (simulatorPrefab == null) return;
            if (!Application.isEditor && !allowInBuilds) return;
            if (HeadsetRunning()) return;

            Instantiate(simulatorPrefab);
        }

        static bool HeadsetRunning()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
                if (display.running) return true;
            return false;
        }
    }
}
