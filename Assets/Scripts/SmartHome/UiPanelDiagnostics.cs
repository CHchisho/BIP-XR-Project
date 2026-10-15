using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace SmartHouse
{
    /// <summary>
    /// Debug aid for the wall panels: every <see cref="Interval"/> seconds it logs, for each world-space UI canvas,
    /// where its text is, where the board behind it is, the gap between the two, and where each eye should see it.
    /// The same lines go to a file, together with every warning and error from the Console:
    /// in the editor Logs/ui_panels.log in the project folder, on the Quest the app's persistent data folder.
    /// Installs itself in every play session; set <see cref="Enabled"/> to false to switch it off.
    /// </summary>
    public class UiPanelDiagnostics : MonoBehaviour
    {
        public static bool Enabled = true;
        const float Interval = 3f;

        string path;
        float next;
        bool reportedPipeline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Enabled) return;
            var go = new GameObject("UI Panel Diagnostics");
            DontDestroyOnLoad(go);
            go.AddComponent<UiPanelDiagnostics>();
        }

        void Awake()
        {
            string folder = Application.isEditor ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs") : Application.persistentDataPath;
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, "ui_panels.log");
            File.WriteAllText(path, $"UI panel diagnostics, started {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            Debug.Log($"[UiPanelDiagnostics] Logging UI panel positions every {Interval:0} s to {path}");
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return; // our own reports are written directly; copy only warnings and errors
            Append($"[{Time.realtimeSinceStartup,7:F1}s] {type}: {message}\n" + (type == LogType.Warning ? "" : stackTrace));
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + Interval;

            if (!reportedPipeline && XRSettings.isDeviceActive)
            {
                reportedPipeline = true;
                XrRenderQuality.ReportPipeline(); // only now is the stereo mode the one the headset really uses
            }

            var report = BuildReport();
            Debug.Log(report);
            Append(report + "\n");
        }

        static string BuildReport()
        {
            var cam = Camera.main;
            var sb = new StringBuilder();
            sb.Append($"[UiPanelDiagnostics] t={Time.realtimeSinceStartup:F1}s  XR active={XRSettings.isDeviceActive}  stereo={XRSettings.stereoRenderingMode}");
            if (cam == null) return sb.Append("  (no main camera)").ToString();
            sb.Append($"  camera pos={F(cam.transform.position)} fwd={F(cam.transform.forward)} stereoEnabled={cam.stereoEnabled}");

            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
                var rect = (RectTransform)canvas.transform;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var textCenter = (corners[0] + corners[2]) * 0.5f;
                var owner = canvas.transform.parent != null ? canvas.transform.parent : canvas.transform;

                // The board: the owner's own renderers, not anything the canvas draws.
                Bounds board = default;
                bool hasBoard = false;
                foreach (var r in owner.GetComponentsInChildren<Renderer>())
                {
                    if (r.transform.IsChildOf(canvas.transform)) continue;
                    if (!hasBoard) { board = r.bounds; hasBoard = true; }
                    else board.Encapsulate(r.bounds);
                }

                sb.Append($"\n  {owner.name}: text={F(textCenter)}");
                if (hasBoard) sb.Append($" board={F(board.center)} text-board={F(textCenter - board.center)}");
                sb.Append($" dist={Vector3.Distance(cam.transform.position, textCenter):F2}m");
                if (cam.stereoEnabled)
                {
                    var left = cam.WorldToViewportPoint(textCenter, Camera.MonoOrStereoscopicEye.Left);
                    var right = cam.WorldToViewportPoint(textCenter, Camera.MonoOrStereoscopicEye.Right);
                    sb.Append($" viewport L={F2(left)} R={F2(right)}");
                }
                else sb.Append($" viewport={F2(cam.WorldToViewportPoint(textCenter))}");
            }
            return sb.ToString();
        }

        void Append(string text)
        {
            try { File.AppendAllText(path, text); }
            catch (IOException) { } // never let logging break the app
        }

        static string F(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
        static string F2(Vector3 v) => $"({v.x:F2}, {v.y:F2}{(v.z < 0f ? ", behind" : "")})";
    }
}
