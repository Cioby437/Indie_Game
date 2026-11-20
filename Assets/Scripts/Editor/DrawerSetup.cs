using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Drawer > Set Up All Drawer Animations
//
// After Wooden_Drawer_Animated.fbx was re-exported with every drawer and the door animated:
//   1. Imports one clip per part (the takes "<part>|<part>Action"; the other takes Blender writes are skipped).
//      The drawer01 clip keeps its name, so everything that used it keeps working.
//   2. Adds every clip to the AnimationScript in the open scene (drawer01 stays animation 0).
//   3. Gives each part except drawer01 (which already has C_Drawer_Cabinet_1) a trigger BoxCollider and a
//      ClickableAnimation, so clicking it opens / closes it.
public static class DrawerSetup
{
    private const string Fbx = "Assets/Objects/WoodenTable_03_4k.fbx/Wooden_Drawer_Animated.fbx";
    private const string FirstDrawer = "WoodenTable_03_drawer01";

    [MenuItem("Tools/Drawer/Set Up All Drawer Animations")]
    private static void Run()
    {
        List<string> log = new List<string>();

        // ------------------------------------------------ 1. clips
        ModelImporter importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Drawer", "Not found: " + Fbx, "OK");
            return;
        }

        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;

        List<ModelImporterClipAnimation> clips = new List<ModelImporterClipAnimation>();
        foreach (ModelImporterClipAnimation take in importer.defaultClipAnimations)
        {
            string[] parts = take.takeName.Split('|');
            if (parts.Length != 2 || parts[0] + "Action" != parts[1])
            {
                continue; // e.g. drawer02's motion applied to drawer03
            }

            take.name = take.takeName;
            take.loopTime = false;
            clips.Add(take);
            log.Add("Clip " + take.name + " (frames " + take.firstFrame + "-" + take.lastFrame + ")");
        }

        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("Drawer", "No '<part>|<part>Action' takes in " + Fbx + ".", "OK");
            return;
        }

        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();

        Dictionary<string, AnimationClip> loaded = AssetDatabase.LoadAllAssetsAtPath(Fbx)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name);

        // ------------------------------------------------ 2. AnimationScript
        AnimationScript script = Object.FindFirstObjectByType<AnimationScript>(FindObjectsInactive.Include);
        if (script == null || script.animator == null)
        {
            EditorUtility.DisplayDialog("Drawer", "No AnimationScript (with an Animator) in the open scene.", "OK");
            return;
        }

        Undo.RecordObject(script, "Drawer Setup");

        List<AnimationScript.AnimationData> list =
            (script.animations ?? new AnimationScript.AnimationData[0]).ToList();

        // drawer01 first, then the others in name order.
        foreach (string clipName in loaded.Keys.OrderBy(n => n.StartsWith(FirstDrawer) ? 0 : 1).ThenBy(n => n))
        {
            AnimationScript.AnimationData entry = list.FirstOrDefault(a => a != null && a.animationStateName == clipName);
            if (entry == null)
            {
                entry = new AnimationScript.AnimationData { animationStateName = clipName, canReverse = true };
                list.Add(entry);
                log.Add("AnimationScript: added " + clipName);
            }

            entry.animationClip = loaded[clipName];
        }

        script.animations = list.ToArray();
        EditorUtility.SetDirty(script);

        // ------------------------------------------------ 3. click colliders
        Transform root = script.animator.transform;

        foreach (string clipName in loaded.Keys)
        {
            string partName = clipName.Split('|')[0];
            if (partName == FirstDrawer)
            {
                continue; // has C_Drawer_Cabinet_1 (opens it and switches to the cabinet camera)
            }

            Transform part = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == partName);
            if (part == null)
            {
                log.Add("WARNING: no object named " + partName + " under " + root.name);
                continue;
            }

            BoxCollider box = part.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(part.gameObject); // sizes itself to the mesh
                log.Add(partName + ": added a trigger BoxCollider");
            }
            else
            {
                Undo.RecordObject(box, "Drawer Setup");
            }

            box.isTrigger = true;

            // Always fit the mesh (it changes if the FBX is re-exported with another scale).
            MeshFilter meshFilter = part.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                box.center = meshFilter.sharedMesh.bounds.center;
                box.size = meshFilter.sharedMesh.bounds.size;
            }

            ClickableAnimation clickable = part.GetComponent<ClickableAnimation>();
            if (clickable == null)
            {
                clickable = Undo.AddComponent<ClickableAnimation>(part.gameObject);
            }
            else
            {
                Undo.RecordObject(clickable, "Drawer Setup");
            }

            clickable.animationScript = script;
            clickable.animationName = clipName;
            EditorUtility.SetDirty(clickable);
            log.Add(partName + ": clickable (" + clipName + ")");
        }

        EditorSceneManager.MarkSceneDirty(script.gameObject.scene);

        Debug.Log("[Drawer Setup]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Drawer", "Done. Check the Console, then save the scene.", "OK");
    }
}
