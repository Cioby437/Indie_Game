using System.Collections.Generic;
using FMODUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Cassette > Remove Recorder And Dialogue Leftovers
//
// The recording feature (Recorder_Script + the Scriptable_1 programmer instrument) and the TV dialogue
// system (DialogueManager) were removed. This cleans the open scenes of what they left behind:
//   - "Missing (Mono Script)" components (where Recorder_Script / DialogueManager used to be)
//   - FMOD Event Emitters that play Scriptable_1 (e.g. the Recorder_PlayBack object). If the emitter is the
//     only thing on its object, the whole object is deleted; otherwise just the emitter component.
// One undo step (Ctrl+Z) reverts it. Save the scene afterwards.
public static class RecorderCleanup
{
    private const string PlaybackEventName = "Scriptable_1";

    [MenuItem("Tools/Cassette/Remove Recorder And Dialogue Leftovers")]
    private static void Run()
    {
        Undo.SetCurrentGroupName("Remove Recorder And Dialogue Leftovers");
        int group = Undo.GetCurrentGroup();

        List<string> log = new List<string>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (missing > 0)
                    {
                        Undo.RegisterCompleteObjectUndo(t.gameObject, "Remove Recorder And Dialogue Leftovers");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                        log.Add("Removed " + missing + " missing script(s) from '" + t.name + "'.");
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
                }

                foreach (StudioEventEmitter emitter in root.GetComponentsInChildren<StudioEventEmitter>(true))
                {
                    string path = emitter.EventReference.Path;
                    if (string.IsNullOrEmpty(path) || !path.EndsWith("/" + PlaybackEventName)) continue;

                    GameObject go = emitter.gameObject;
                    bool onlyEmitter = go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2;

                    if (onlyEmitter)
                    {
                        log.Add("Deleted '" + go.name + "' (it only played " + PlaybackEventName + ").");
                        Undo.DestroyObjectImmediate(go);
                    }
                    else
                    {
                        log.Add("Removed the " + PlaybackEventName + " emitter from '" + go.name + "'.");
                        Undo.DestroyObjectImmediate(emitter);
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }

        Undo.CollapseUndoOperations(group);

        string message = log.Count == 0 ? "Nothing left to clean up." : string.Join("\n", log);
        Debug.Log("[Leftover Cleanup]\n" + message);
        EditorUtility.DisplayDialog("Remove Recorder And Dialogue Leftovers", message + "\n\nSave the scene to keep the changes.", "OK");
    }
}
