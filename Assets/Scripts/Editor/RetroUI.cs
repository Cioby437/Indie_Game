using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The RetroLook post-process only affects what the camera renders. "Screen Space - Overlay" canvases are drawn
// on top of everything afterwards, so in play mode the UI stays crisp (the Scene view shows it with the effect
// because there the canvas is drawn as part of the scene).
//
// Tools > UI > Render UI Through Retro Look
//   Switches every Overlay canvas in the open scene to "Screen Space - Camera" on the game camera,
//   just in front of its near clip plane, so the UI gets the same pixels / dither / grade as the game.
// Tools > UI > Render UI On Top (No Retro Look)
//   Switches them back to Overlay.
public static class RetroUI
{
    // Distance in front of the near clip plane. Must stay closer than anything held in front of the camera.
    private const float PlaneOffset = 0.02f;

    [MenuItem("Tools/UI/Render UI Through Retro Look")]
    private static void ThroughCamera()
    {
        Camera cam = FindGameCamera();
        if (cam == null)
        {
            EditorUtility.DisplayDialog("UI", "No game camera (CinemachineBrain / MainCamera) found in the open scene.", "OK");
            return;
        }

        int count = 0;
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            Undo.RecordObject(canvas, "Render UI Through Retro Look");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + PlaneOffset;
            EditorUtility.SetDirty(canvas);
            count++;

            Debug.Log("[UI] " + canvas.name + " now renders through " + cam.name +
                      " at " + canvas.planeDistance.ToString("0.00") + " m (gets the Retro Look).", canvas);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("UI", count + " canvas(es) switched to Screen Space - Camera. Save the scene to keep it.", "OK");
    }

    [MenuItem("Tools/UI/Render UI On Top (No Retro Look)")]
    private static void Overlay()
    {
        int count = 0;
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceCamera) continue;

            Undo.RecordObject(canvas, "Render UI On Top");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            EditorUtility.SetDirty(canvas);
            count++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("UI", count + " canvas(es) switched back to Overlay. Save the scene to keep it.", "OK");
    }

    // Texts and images that aren't part of a button still catch clicks (Raycast Target on), so an invisible or
    // decorative element over the scene swallows the click before CursorCameraManager sees it.
    [MenuItem("Tools/UI/Stop Non-Button UI From Blocking Clicks")]
    private static void NonButtonsIgnoreClicks()
    {
        int count = 0;
        foreach (UnityEngine.UI.Graphic graphic in Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!graphic.raycastTarget) continue;
            if (graphic.GetComponentInParent<UnityEngine.UI.Selectable>(true) != null) continue; // buttons, sliders, ...

            Undo.RecordObject(graphic, "Stop UI Blocking Clicks");
            graphic.raycastTarget = false;
            EditorUtility.SetDirty(graphic);
            count++;
            Debug.Log("[UI] Raycast Target off: " + graphic.name, graphic);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("UI", count + " non-button UI element(s) no longer block clicks. Save the scene to keep it.", "OK");
    }

    private static Camera FindGameCamera()
    {
        CinemachineBrain brain = Object.FindFirstObjectByType<CinemachineBrain>(FindObjectsInactive.Include);
        if (brain != null && brain.GetComponent<Camera>() != null)
        {
            return brain.GetComponent<Camera>();
        }

        return Camera.main;
    }
}
