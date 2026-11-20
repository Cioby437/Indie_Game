using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

// Tools > Optimization > Set Up And Bake Occlusion Culling
//
// Unity already skips everything outside the current camera's view (frustum culling, automatic).
// This adds occlusion culling: objects hidden BEHIND walls/furniture from the current view aren't drawn either.
//
// It marks objects that never move as:
//   Occludee Static  - can be hidden by occluders (all non-moving renderers + the terrain)
//   Occluder Static  - can hide other things (only big, solid, always-present ones: walls, floor, big furniture)
// Moving / animated objects (anything under an Animator, Animation, Rigidbody, Timeline, camera rig),
// objects switched on/off by the stages and inactive objects are left alone. They are still culled
// at runtime when a static occluder hides them (Dynamic Occlusion).
// Then it bakes. Only the static flags change: lighting, batching and everything else stay the same.
//
// Tools > Optimization > Clear Occlusion Culling undoes all of it.
public static class OcclusionCullingSetup
{
    // An occluder must be at least this big (largest side, metres).
    private const float MinOccluderSize = 1.0f;

    // Bake settings for an indoor room (Unity's defaults are tuned for big outdoor levels).
    private const float SmallestOccluder = 1.0f;
    private const float SmallestHole = 0.05f;
    private const float BackfaceThreshold = 100f;

    private const StaticEditorFlags OcclusionFlags =
        StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

    [MenuItem("Tools/Optimization/Set Up And Bake Occlusion Culling")]
    private static void SetUpAndBake()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Occlusion Culling", "Leave play mode first.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        HashSet<GameObject> stageToggled = StageToggledObjects(scene);

        int occluders = 0;
        int occludees = 0;
        int skippedMoving = 0;

        foreach (Renderer renderer in AllInScene<Renderer>(scene))
        {
            if (!(renderer is MeshRenderer))
            {
                continue; // skinned meshes, particles, lines, trails: always dynamic
            }

            GameObject go = renderer.gameObject;

            if (!go.activeInHierarchy || IsMoving(go.transform) || IsUnder(go.transform, stageToggled))
            {
                skippedMoving++;
                continue;
            }

            StaticEditorFlags flags = StaticEditorFlags.OccludeeStatic;
            occludees++;

            if (IsGoodOccluder(renderer))
            {
                flags |= StaticEditorFlags.OccluderStatic;
                occluders++;
            }

            SetOcclusionFlags(go, flags);
        }

        foreach (Terrain terrain in AllInScene<Terrain>(scene))
        {
            if (terrain.gameObject.activeInHierarchy && !IsUnder(terrain.transform, stageToggled))
            {
                SetOcclusionFlags(terrain.gameObject, OcclusionFlags);
                occluders++;
                occludees++;
            }
        }

        foreach (Camera camera in AllInScene<Camera>(scene))
        {
            if (!camera.useOcclusionCulling)
            {
                Undo.RecordObject(camera, "Occlusion Culling");
                camera.useOcclusionCulling = true;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
        StaticOcclusionCulling.smallestHole = SmallestHole;
        StaticOcclusionCulling.backfaceThreshold = BackfaceThreshold;

        bool baked = StaticOcclusionCulling.Compute();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string report =
            occluders + " occluders, " + occludees + " occludees, " +
            skippedMoving + " moving/toggled renderers left dynamic. " +
            (baked ? "Bake finished." : "Bake FAILED, see the Console.");

        Debug.Log("[Occlusion Culling] " + report);
        EditorUtility.DisplayDialog("Occlusion Culling", report +
            "\n\nCheck it in Window > Rendering > Occlusion Culling > Visualization.", "OK");
    }

    [MenuItem("Tools/Optimization/Clear Occlusion Culling")]
    private static void Clear()
    {
        Scene scene = SceneManager.GetActiveScene();

        foreach (Transform t in AllInScene<Transform>(scene))
        {
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
            if ((flags & OcclusionFlags) != 0)
            {
                Undo.RecordObject(t.gameObject, "Clear Occlusion Culling");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~OcclusionFlags);
            }
        }

        StaticOcclusionCulling.Clear();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Occlusion Culling] Cleared flags and baked data. Save the scene to keep it.");
    }

    // -------------------------------------------------------------------------------------------

    private static void SetOcclusionFlags(GameObject go, StaticEditorFlags occlusion)
    {
        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
        StaticEditorFlags wanted = (flags & ~OcclusionFlags) | occlusion;

        if (wanted != flags)
        {
            Undo.RecordObject(go, "Occlusion Culling");
            GameObjectUtility.SetStaticEditorFlags(go, wanted);
        }
    }

    // Anything that can move: animated, physics, timeline, or part of a camera rig.
    private static bool IsMoving(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.GetComponent<Animator>() != null ||
                p.GetComponent<Animation>() != null ||
                p.GetComponent<Rigidbody>() != null ||
                p.GetComponent<PlayableDirector>() != null ||
                p.GetComponent<CinemachineVirtualCameraBase>() != null ||
                p.GetComponent<Camera>() != null)
            {
                return true;
            }
        }

        return false;
    }

    // Big, opaque and solid enough to hide things behind it.
    private static bool IsGoodOccluder(Renderer renderer)
    {
        Vector3 size = renderer.bounds.size;
        if (Mathf.Max(size.x, size.y, size.z) < MinOccluderSize)
        {
            return false;
        }

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null)
            {
                continue;
            }

            bool transparent =
                material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest ||
                material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                material.IsKeywordEnabled("_ALPHATEST_ON");

            if (transparent)
            {
                return false; // glass, cut-out leaves, decals, etc. don't block the view
            }
        }

        return true;
    }

    // Objects the stages switch on/off: they can't be static occluders (they'd hide things while gone).
    private static HashSet<GameObject> StageToggledObjects(Scene scene)
    {
        HashSet<GameObject> result = new HashSet<GameObject>();

        foreach (GameStage stage in AllInScene<GameStage>(scene))
        {
            foreach (GameObject go in (stage.activateOnEnter ?? new GameObject[0])
                     .Concat(stage.deactivateOnEnter ?? new GameObject[0]))
            {
                if (go != null) result.Add(go);
            }
        }

        return result;
    }

    private static bool IsUnder(Transform t, HashSet<GameObject> roots)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (roots.Contains(p.gameObject)) return true;
        }

        return false;
    }

    private static IEnumerable<T> AllInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
