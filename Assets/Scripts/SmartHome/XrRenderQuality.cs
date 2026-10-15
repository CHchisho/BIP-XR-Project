using UnityEngine;
using UnityEngine.XR;

namespace SmartHouse
{
    /// <summary>
    /// Renders the headset's eye images slightly above native resolution (supersampling), so text and
    /// thin lines stay sharp. DLSS isn't available in URP; this plus 4x MSAA is the URP way.
    /// Kept modest: an RTX 3050 laptop is at the low end for Quest Link.
    /// </summary>
    static class XrRenderQuality
    {
        const float QuestScale = 1.2f;
        const float LinkScale = 1.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            if (!XrPresence.HeadsetActive()) return;
            XRSettings.eyeTextureResolutionScale = Application.platform == RuntimePlatform.Android ? QuestScale : LinkScale;
        }

        /// <summary>
        /// Logs which pipeline asset the headset renders with. The GPU Resident Drawer must be off: in this
        /// Unity version its per-frame update throws (GetActiveBuildProfile on a worker thread) and then
        /// draws non-static meshes (the wall panels, the controller models) with stale data, in one eye only.
        /// Called by <see cref="UiPanelDiagnostics"/> once the headset is running, because before that the
        /// stereo mode still reads MultiPass.
        /// </summary>
        public static void ReportPipeline()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var urp = asset as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            string grd = urp != null ? urp.gpuResidentDrawerMode.ToString() : "n/a";
            string line = $"[XrRenderQuality] Quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', " +
                          $"pipeline asset '{(asset != null ? asset.name : "none (built-in)")}', GPU Resident Drawer: {grd}, " +
                          $"stereo mode: {XRSettings.stereoRenderingMode}.";
            if (urp == null || urp.gpuResidentDrawerMode != UnityEngine.Rendering.GPUResidentDrawerMode.Disabled)
                Debug.LogWarning(line + " Expected a URP asset with the GPU Resident Drawer disabled.");
            else
                Debug.Log(line);
        }
    }
}
