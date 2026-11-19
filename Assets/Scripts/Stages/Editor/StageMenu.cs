using UnityEditor;
using UnityEngine;

// The "Stages" menu in Unity's top menu bar.
//
//   Stages/Cutscene, Stages/Gameplay Loop
//       In play mode: jump to that stage.
//       In edit mode: enter play mode starting at that stage.
//   Stages/Skip Stage Always/<stage>
//       Ticked stages are skipped every time play mode starts (saved per project, on this computer).
//   Stages/Stage Switcher Window
//       Same controls for every stage in the scene, including stages added later.
[InitializeOnLoad]
public static class StageMenu
{
    public const string Cutscene = "Cutscene";
    public const string GameplayLoop = "Gameplay Loop";

    private const string SkipKeyPrefix = "Stages.SkipAlways.";
    private const string RequestKey = "Stages.RequestedStart";

    static StageMenu()
    {
        StageManager.SkipAlwaysProvider = IsSkippedAlways;
        StageManager.RequestedStartProvider = TakeRequestedStart;
    }

    // ---------------------------------------------------------------- API (also used by the window)

    public static bool IsSkippedAlways(string stageName)
    {
        return EditorPrefs.GetBool(SkipKey(stageName), false);
    }

    public static void SetSkippedAlways(string stageName, bool skip)
    {
        EditorPrefs.SetBool(SkipKey(stageName), skip);
    }

    public static bool IsCurrent(string stageName)
    {
        return EditorApplication.isPlaying &&
               StageManager.Instance != null &&
               StageManager.Instance.Current != null &&
               StageManager.Instance.Current.StageName == stageName;
    }

    public static void PlayStage(string stageName)
    {
        if (EditorApplication.isPlaying)
        {
            if (StageManager.Instance == null)
            {
                Debug.LogWarning("[Stages] There is no StageManager in the playing scene. " +
                                 "Use Stages > Setup > Build Cutscene Rig In Open Scene first.");
                return;
            }

            StageManager.Instance.GoTo(stageName);
            return;
        }

        // Start play mode at this stage (even if it is marked "Skip Stage Always").
        SessionState.SetString(RequestKey, stageName);
        EditorApplication.EnterPlaymode();
    }

    private static string SkipKey(string stageName)
    {
        return SkipKeyPrefix + PlayerSettings.productGUID + "." + stageName;
    }

    private static string TakeRequestedStart()
    {
        string requested = SessionState.GetString(RequestKey, "");
        SessionState.EraseString(RequestKey);
        return requested;
    }

    // ---------------------------------------------------------------- menu items

    [MenuItem("Stages/" + Cutscene, false, 0)]
    private static void PlayCutscene() { PlayStage(Cutscene); }

    [MenuItem("Stages/" + Cutscene, true)]
    private static bool PlayCutsceneValidate()
    {
        Menu.SetChecked("Stages/" + Cutscene, IsCurrent(Cutscene));
        return true;
    }

    [MenuItem("Stages/" + GameplayLoop, false, 1)]
    private static void PlayGameplayLoop() { PlayStage(GameplayLoop); }

    [MenuItem("Stages/" + GameplayLoop, true)]
    private static bool PlayGameplayLoopValidate()
    {
        Menu.SetChecked("Stages/" + GameplayLoop, IsCurrent(GameplayLoop));
        return true;
    }

    [MenuItem("Stages/Next Stage", false, 2)]
    private static void NextStage()
    {
        if (StageManager.Instance != null) StageManager.Instance.Next();
    }

    [MenuItem("Stages/Next Stage", true)]
    private static bool NextStageValidate()
    {
        return EditorApplication.isPlaying && StageManager.Instance != null;
    }

    [MenuItem("Stages/Skip Stage Always/" + Cutscene, false, 20)]
    private static void ToggleSkipCutscene() { SetSkippedAlways(Cutscene, !IsSkippedAlways(Cutscene)); }

    [MenuItem("Stages/Skip Stage Always/" + Cutscene, true)]
    private static bool ToggleSkipCutsceneValidate()
    {
        Menu.SetChecked("Stages/Skip Stage Always/" + Cutscene, IsSkippedAlways(Cutscene));
        return true;
    }

    [MenuItem("Stages/Skip Stage Always/" + GameplayLoop, false, 21)]
    private static void ToggleSkipGameplayLoop() { SetSkippedAlways(GameplayLoop, !IsSkippedAlways(GameplayLoop)); }

    [MenuItem("Stages/Skip Stage Always/" + GameplayLoop, true)]
    private static bool ToggleSkipGameplayLoopValidate()
    {
        Menu.SetChecked("Stages/Skip Stage Always/" + GameplayLoop, IsSkippedAlways(GameplayLoop));
        return true;
    }

    [MenuItem("Stages/Stage Switcher Window", false, 40)]
    private static void OpenWindow()
    {
        StageSwitcherWindow.Open();
    }
}
