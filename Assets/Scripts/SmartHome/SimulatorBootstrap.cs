using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Spawns the XR Device Simulator when no headset is in use, so the app can be
    /// tested with mouse and keyboard. Never alongside a real headset: its simulated
    /// controllers would fight the real ones.
    /// </summary>
    public class SimulatorBootstrap : MonoBehaviour
    {
        [SerializeField] GameObject simulatorPrefab;
        [SerializeField] bool allowInBuilds;

        void Start()
        {
            if (simulatorPrefab == null) return;
            if (!Application.isEditor && !allowInBuilds) return;
            if (XrPresence.HeadsetActive()) return;

            Instantiate(simulatorPrefab);
        }
    }
}
