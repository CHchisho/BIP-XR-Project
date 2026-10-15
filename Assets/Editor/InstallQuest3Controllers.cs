using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SmartHouse.EditorTools
{
    /// <summary>
    /// Replaces the XRI starter-asset "UniversalController" visuals with the Meta Quest 3 (Touch Plus) models.
    /// Layout under each "&lt;Side&gt; Controller Visual" (kept by name so ControllerRayAnchor still finds it):
    ///   Grip Space  (moved to the live grip pose by MetaQuestControllerVisual)
    ///     MetaQuest3Controller_&lt;Side&gt;  (the FBX, turned 180° because it imports facing -Z)
    /// The old model is switched off rather than deleted because the rig is a prefab instance.
    /// </summary>
    public static class InstallQuest3Controllers
    {
        [MenuItem("Smart House/Install Quest 3 Controllers")]
        public static void Install()
        {
            foreach (var side in new[] { "Left", "Right" })
            {
                var visual = GameObject.Find($"XR Origin (XR Rig)/Camera Offset/{side} Controller/{side} Controller Visual");
                if (visual == null) { Debug.LogError($"[SmartHouse] {side} Controller Visual not found."); continue; }

                Undo.RecordObject(visual.transform, "Install Quest 3 controllers");
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                var old = visual.transform.Find("UniversalController");
                if (old != null) { Undo.RecordObject(old.gameObject, "Install Quest 3 controllers"); old.gameObject.SetActive(false); }
                var animator = visual.GetComponent("ControllerAnimator") as Behaviour;
                if (animator != null) { Undo.RecordObject(animator, "Install Quest 3 controllers"); animator.enabled = false; }

                var existing = visual.transform.Find("Grip Space");
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
                var grip = new GameObject("Grip Space");
                Undo.RegisterCreatedObjectUndo(grip, "Install Quest 3 controllers");
                grip.transform.SetParent(visual.transform, false);

                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Controllers/MetaQuest3Controller_{side}.fbx");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, grip.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.gameObject.layer = visual.layer;
                }

                var driver = visual.GetComponent<MetaQuestControllerVisual>() ?? Undo.AddComponent<MetaQuestControllerVisual>(visual);
                var so = new SerializedObject(driver);
                so.FindProperty("leftHand").boolValue = side == "Left";
                so.FindProperty("model").objectReferenceValue = grip.transform;
                so.ApplyModifiedProperties();
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[SmartHouse] Quest 3 controllers installed.");
        }
    }
}
