using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Cassette > Set Up Separate Cassette Player
//
// Swaps the old combined Casette_Animated model for the separate exports in Assets/Objects/Cassette:
//   - Cassette_Player.fbx (player + lid) goes where the old player was, inside a "Casette_Player_Root" object
//     that takes over the old root's place, Recorder_Script, C_CasettePlayer collider and Recorder_PlayBack.
//     Every reference to the old root / Recorder_Script is pointed at the new ones.
//   - One Cassette.fbx is placed next to it (where the cassette sits in Blender), with a CassetteItem,
//     a click collider and its Insert / Eject clips. Duplicate it (Ctrl+D) for more cassettes.
//   - The old Casette_Animated is switched off (not deleted), so you can compare and delete it yourself.
// One undo step (Ctrl+Z) reverts the scene part.
public static class CassetteSetup
{
    private const string Folder = "Assets/Objects/Cassette/";
    private const string PlayerFbx = Folder + "Cassette_Player.fbx";
    private const string CassetteFbx = Folder + "Cassette.fbx";

    // Frames of the "Combined" actions in Casette_Animated.blend (markers Insert = 1, Eject = 93, end = 173).
    // Cassette_Player.fbx (lid) and Cassette.fbx (cassette) use the same ranges, so they stay in sync.
    private const float InsertFirst = 1f, InsertLast = 93f;
    private const float EjectFirst = 93f, EjectLast = 173f;

    private const string PlayerMeshName = "portable_cassette_player";
    private const string RootName = "Casette_Player_Root";

    [MenuItem("Tools/Cassette/Set Up Separate Cassette Player")]
    private static void Run()
    {
        CasettePlayerInspect inspect = Object.FindFirstObjectByType<CasettePlayerInspect>(FindObjectsInactive.Include);
        if (inspect == null || inspect.casettePlayer == null)
        {
            EditorUtility.DisplayDialog("Cassette", "No CasettePlayerInspect with a Casette Player in the open scene.", "OK");
            return;
        }

        GameObject old = inspect.casettePlayer;
        if (old.name == RootName)
        {
            EditorUtility.DisplayDialog("Cassette", "Already set up (the Casette Player is " + RootName + ").", "OK");
            return;
        }

        List<string> log = new List<string>();

        // ------------------------------------------------ 1. import settings + materials of the old model
        Dictionary<string, Material> materials = CollectOldMaterials(old);
        ConfigurePlayerImporter(materials, log);
        ConfigureCassetteImporter(materials, log);

        GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbx);
        GameObject cassetteAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CassetteFbx);
        AnimationClip insert = LoadClip("Insert");
        AnimationClip eject = LoadClip("Eject");

        if (playerAsset == null || cassetteAsset == null)
        {
            EditorUtility.DisplayDialog("Cassette", "Cassette_Player.fbx / Cassette.fbx not found in " + Folder, "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Up Separate Cassette Player");

        // ------------------------------------------------ 2. new root in the old root's place
        Transform parent = old.transform.parent;

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Cassette");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = old.transform.localPosition;
        root.transform.localRotation = old.transform.localRotation;
        root.transform.localScale = old.transform.localScale;
        root.transform.SetSiblingIndex(old.transform.GetSiblingIndex() + 1);

        // ------------------------------------------------ 3. the new player model, lined up with the old one
        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset, root.scene);
        Undo.RegisterCreatedObjectUndo(player, "Cassette");
        player.name = "Cassette_Player";
        player.transform.SetParent(root.transform, false);

        Renderer oldMesh = FindPlayerRenderer(old.transform);
        Renderer newMesh = FindPlayerRenderer(player.transform);

        if (oldMesh != null && newMesh != null)
        {
            AlignTo(player.transform, newMesh, oldMesh, log);
        }
        else
        {
            log.Add("WARNING: couldn't find the player mesh in the old or new model; Cassette_Player is at the old root's position, check it.");
        }

        // ------------------------------------------------ 4. carry over what was added to the old model
        Dictionary<Object, Object> remap = new Dictionary<Object, Object>
        {
            { old, root },
            { old.transform, root.transform }
        };

        foreach (Transform t in old.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (t != old.transform && PrefabUtility.IsAddedGameObjectOverride(t.gameObject))
            {
                Undo.SetTransformParent(t, root.transform, "Cassette");
                log.Add("Moved " + t.name + " to " + RootName);
            }
        }

        foreach (Component c in old.GetComponents<Component>())
        {
            if (c == null || c is Transform || !PrefabUtility.IsAddedComponentOverride(c))
            {
                continue;
            }

            Component copy = Undo.AddComponent(root, c.GetType());
            EditorUtility.CopySerialized(c, copy);
            remap[c] = copy;
            log.Add("Copied " + c.GetType().Name + " to " + RootName);
        }

        int repointed = RemapReferences(root.scene, remap, old);
        log.Add(repointed + " references re-pointed from the old model to " + RootName);

        Undo.RecordObject(inspect, "Cassette");
        inspect.casettePlayer = root;
        inspect.cassetteSocket = player.transform;
        EditorUtility.SetDirty(inspect);

        // ------------------------------------------------ 5. first cassette, where it sits in Blender
        GameObject cassette = (GameObject)PrefabUtility.InstantiatePrefab(cassetteAsset, root.scene);
        Undo.RegisterCreatedObjectUndo(cassette, "Cassette");
        cassette.name = "Cassette_01";
        cassette.transform.SetParent(parent, true);
        cassette.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
        cassette.transform.localScale = WorldToLocalScale(player.transform.lossyScale, parent);
        cassette.transform.SetSiblingIndex(root.transform.GetSiblingIndex() + 1);

        if (cassette.GetComponent<Animator>() == null)
        {
            Undo.AddComponent<Animator>(cassette);
        }

        CassetteItem item = Undo.AddComponent<CassetteItem>(cassette);
        item.insertClip = insert;
        item.ejectClip = eject;
        item.cassetteId = "Cassette_01";

        foreach (MeshFilter mf in cassette.GetComponentsInChildren<MeshFilter>(true))
        {
            BoxCollider box = Undo.AddComponent<BoxCollider>(mf.gameObject); // sizes itself to the mesh
            box.isTrigger = true;
        }

        if (insert == null || eject == null)
        {
            log.Add("WARNING: Insert / Eject clips not found in Cassette.fbx. Assign them on Cassette_01's CassetteItem.");
        }

        // ------------------------------------------------ 6. retire the old model
        Undo.RecordObject(old, "Cassette");
        old.SetActive(false);
        old.name = "OLD_Casette_Animated (unused, delete when happy)";

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log("[Cassette Setup]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Cassette",
            "Done. Check the Console for details, then save the scene.\n\n" +
            "Cassette_01 is next to the player. Duplicate it (Ctrl+D) and move the copies for more cassettes.", "OK");
    }

    // ------------------------------------------------------------------------------------ import

    private static void ConfigurePlayerImporter(Dictionary<string, Material> materials, List<string> log)
    {
        ModelImporter importer = AssetImporter.GetAtPath(PlayerFbx) as ModelImporter;
        if (importer == null) return;

        ConfigureAnimatedImporter(importer, PlayerFbx, log);
        RemapMaterials(importer, PlayerFbx, materials, log);
        importer.SaveAndReimport();
    }

    private static void ConfigureCassetteImporter(Dictionary<string, Material> materials, List<string> log)
    {
        ModelImporter importer = AssetImporter.GetAtPath(CassetteFbx) as ModelImporter;
        if (importer == null) return;

        ConfigureAnimatedImporter(importer, CassetteFbx, log);
        RemapMaterials(importer, CassetteFbx, materials, log);
        importer.SaveAndReimport();
    }

    // Keeps the file root (the clips animate the meshes relative to it), Generic animation, Insert / Eject clips.
    private static void ConfigureAnimatedImporter(ModelImporter importer, string path, List<string> log)
    {
        importer.preserveHierarchy = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.SaveAndReimport();

        ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
        if (defaults.Length == 0)
        {
            log.Add("WARNING: " + System.IO.Path.GetFileName(path) + " has no animation.");
            return;
        }

        importer.clipAnimations = new[]
        {
            MakeClip(defaults[0], "Insert", InsertFirst, InsertLast),
            MakeClip(defaults[0], "Eject", EjectFirst, EjectLast)
        };

        log.Add(System.IO.Path.GetFileName(path) + " clips: Insert " + InsertFirst + "-" + InsertLast +
                ", Eject " + EjectFirst + "-" + EjectLast);
    }

    // Tools > Cassette > Update Cassette Animations
    // After re-exporting the FBX files: re-applies the import settings and clip ranges, and assigns the clips
    // (lid clips on CasettePlayerInspect, cassette clips on every CassetteItem in the open scene).
    [MenuItem("Tools/Cassette/Update Cassette Animations")]
    private static void UpdateAnimations()
    {
        List<string> log = new List<string>();
        Dictionary<string, Material> none = new Dictionary<string, Material>();

        ConfigurePlayerImporter(none, log);
        ConfigureCassetteImporter(none, log);

        AnimationClip playerInsert = LoadClip(PlayerFbx, "Insert");
        AnimationClip playerEject = LoadClip(PlayerFbx, "Eject");
        AnimationClip cassetteInsert = LoadClip(CassetteFbx, "Insert");
        AnimationClip cassetteEject = LoadClip(CassetteFbx, "Eject");

        CasettePlayerInspect inspect = Object.FindFirstObjectByType<CasettePlayerInspect>(FindObjectsInactive.Include);
        if (inspect != null)
        {
            Undo.RecordObject(inspect, "Update Cassette Animations");
            inspect.playerInsertClip = playerInsert;
            inspect.playerEjectClip = playerEject;
            EditorUtility.SetDirty(inspect);
            log.Add("Lid clips assigned on " + inspect.name + "'s CasettePlayerInspect.");

            if (inspect.cassetteSocket != null && inspect.cassetteSocket.GetComponent<Animator>() == null)
            {
                Undo.AddComponent<Animator>(inspect.cassetteSocket.gameObject);
                log.Add("Added an Animator to " + inspect.cassetteSocket.name + " (for the lid).");
            }
        }
        else
        {
            log.Add("WARNING: no CasettePlayerInspect in the open scene.");
        }

        foreach (CassetteItem item in Object.FindObjectsByType<CassetteItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(item, "Update Cassette Animations");
            item.insertClip = cassetteInsert;
            item.ejectClip = cassetteEject;
            EditorUtility.SetDirty(item);
            log.Add("Cassette clips assigned on " + item.name + ".");
        }

        if (inspect != null)
        {
            EditorSceneManager.MarkSceneDirty(inspect.gameObject.scene);
        }

        Debug.Log("[Cassette Animations]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Cassette", "Done. Check the Console, then save the scene.", "OK");
    }

    private static ModelImporterClipAnimation MakeClip(ModelImporterClipAnimation source, string name, float first, float last)
    {
        return new ModelImporterClipAnimation
        {
            name = name,
            takeName = source.takeName,
            firstFrame = first,
            lastFrame = last,
            loopTime = false,
            lockRootRotation = false,
            lockRootHeightY = false,
            lockRootPositionXZ = false
        };
    }

    // The old model's materials, by the name of the material the old FBX brought in.
    private static Dictionary<string, Material> CollectOldMaterials(GameObject old)
    {
        Dictionary<string, Material> result = new Dictionary<string, Material>();

        foreach (Renderer r in old.GetComponentsInChildren<Renderer>(true))
        {
            Renderer source = PrefabUtility.GetCorrespondingObjectFromSource(r);
            if (source == null) continue;

            Material[] sourceMats = source.sharedMaterials;
            Material[] sceneMats = r.sharedMaterials;

            for (int i = 0; i < Mathf.Min(sourceMats.Length, sceneMats.Length); i++)
            {
                Material m = sceneMats[i] != null ? sceneMats[i] : sourceMats[i];
                if (sourceMats[i] != null && m != null && !result.ContainsKey(sourceMats[i].name))
                {
                    result[sourceMats[i].name] = m;
                }
            }
        }

        return result;
    }

    private static void RemapMaterials(ModelImporter importer, string path, Dictionary<string, Material> materials, List<string> log)
    {
        if (materials.Count == 0) return;

        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
        {
            string name = embedded.name;

            Material target;
            if (!materials.TryGetValue(name, out target))
            {
                bool glass = name.ToLowerInvariant().Contains("glass");
                target = materials.FirstOrDefault(kv => kv.Key.ToLowerInvariant().Contains("glass") == glass).Value;
            }

            if (target != null)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), target);
                log.Add(System.IO.Path.GetFileName(path) + ": material " + name + " -> " + target.name);
            }
        }
    }

    private static AnimationClip LoadClip(string name)
    {
        return LoadClip(CassetteFbx, name);
    }

    private static AnimationClip LoadClip(string path, string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name);
    }

    // ------------------------------------------------------------------------------------ scene

    // The player body (not the cassette, not the lid, not added helper objects).
    private static Renderer FindPlayerRenderer(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
            .Where(r => PrefabUtility.GetCorrespondingObjectFromSource(r.gameObject) != null)
            .ToArray();

        Renderer exact = renderers.FirstOrDefault(r => r.name == PlayerMeshName);
        if (exact != null) return exact;

        return renderers
            .Where(r => !r.name.ToLowerInvariant().Contains("cas") || r.name.ToLowerInvariant().Contains("player"))
            .Where(r => !r.name.ToLowerInvariant().Contains("lid"))
            .OrderByDescending(r => r.GetComponent<MeshFilter>().sharedMesh.bounds.size.sqrMagnitude)
            .FirstOrDefault();
    }

    // Moves / turns / scales "model" so its renderer "mine" covers the same place as "target".
    private static void AlignTo(Transform model, Renderer mine, Renderer target, List<string> log)
    {
        // Same orientation as the old mesh.
        model.rotation = target.transform.rotation * Quaternion.Inverse(Quaternion.Inverse(model.rotation) * mine.transform.rotation);

        // Same size (both meshes come from the same Blender object).
        float mineSize = mine.GetComponent<MeshFilter>().sharedMesh.bounds.size.magnitude * MaxAbs(mine.transform.lossyScale);
        float targetSize = target.GetComponent<MeshFilter>().sharedMesh.bounds.size.magnitude * MaxAbs(target.transform.lossyScale);
        if (mineSize > 1e-6f)
        {
            model.localScale *= targetSize / mineSize;
        }

        // Same place.
        model.position += target.bounds.center - mine.bounds.center;

        Vector3 a = target.bounds.size, b = mine.bounds.size;
        bool shapeMatches = Mathf.Abs(a.x - b.x) < 0.2f * a.magnitude &&
                            Mathf.Abs(a.y - b.y) < 0.2f * a.magnitude &&
                            Mathf.Abs(a.z - b.z) < 0.2f * a.magnitude;

        log.Add(shapeMatches
            ? "Cassette_Player lined up with the old player model."
            : "WARNING: the new player's bounds (" + b + ") differ from the old one (" + a + "). " +
              "It may be turned 90 degrees: check Cassette_Player's rotation under " + RootName + ".");
    }

    private static Vector3 WorldToLocalScale(Vector3 worldScale, Transform parent)
    {
        if (parent == null) return worldScale;

        Vector3 p = parent.lossyScale;
        return new Vector3(worldScale.x / p.x, worldScale.y / p.y, worldScale.z / p.z);
    }

    private static float MaxAbs(Vector3 v)
    {
        return Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    // Points every reference to an old object at its replacement.
    private static int RemapReferences(UnityEngine.SceneManagement.Scene scene, Dictionary<Object, Object> remap, GameObject old)
    {
        int count = 0;

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            foreach (Component component in rootObject.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                if (component.transform.IsChildOf(old.transform)) continue; // the old model itself

                SerializedObject so = new SerializedObject(component);
                SerializedProperty property = so.GetIterator();
                bool changed = false;

                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.objectReferenceValue == null)
                    {
                        continue;
                    }

                    if (remap.TryGetValue(property.objectReferenceValue, out Object replacement))
                    {
                        property.objectReferenceValue = replacement;
                        changed = true;
                        count++;
                    }
                }

                if (changed)
                {
                    so.ApplyModifiedProperties();
                }
            }
        }

        return count;
    }
}
