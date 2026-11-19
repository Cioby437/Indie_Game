using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs the game's stages in order: Cutscene -> Gameplay Loop -> ...
//
// In the editor, the "Stages" menu in Unity's top menu bar can:
//   - jump to any stage while playing,
//   - start play mode at a chosen stage,
//   - mark stages as "Skip Stage Always" (they are skipped every time play mode starts).
// Skipped stages still apply their end state, so the scene is set up correctly.
public class StageManager : MonoBehaviour
{
    public static StageManager Instance { get; private set; }

    // Set by the editor menu (Assets/Scripts/Stages/Editor). Null in builds.
    public static Func<string, bool> SkipAlwaysProvider;
    public static Func<string> RequestedStartProvider;

    // Survives a scene reload (used when jumping back to an earlier stage).
    private static string pendingStartStage;

    [Tooltip("Stages in play order.")]
    public List<GameStage> stages = new List<GameStage>();

    [Tooltip("Start the first stage automatically when the scene starts.")]
    public bool playOnStart = true;

    public int CurrentIndex { get; private set; } = -1;

    public GameStage Current
    {
        get { return CurrentIndex >= 0 && CurrentIndex < stages.Count ? stages[CurrentIndex] : null; }
    }

    public event Action<GameStage> StageChanged;

    public static bool IsSkippedAlways(string stageName)
    {
        return SkipAlwaysProvider != null && SkipAlwaysProvider(stageName);
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (!playOnStart || stages.Count == 0)
        {
            return;
        }

        // 1) A stage requested by the editor menu or by a scene reload.
        string requested = pendingStartStage;
        pendingStartStage = null;

        if (string.IsNullOrEmpty(requested) && RequestedStartProvider != null)
        {
            requested = RequestedStartProvider();
        }

        int start = string.IsNullOrEmpty(requested) ? -1 : IndexOf(requested);

        // 2) Otherwise the first stage that isn't marked "Skip Stage Always".
        if (start < 0)
        {
            start = 0;

            while (start < stages.Count - 1 && stages[start] != null && IsSkippedAlways(stages[start].StageName))
            {
                start++;
            }
        }

        GoTo(start);
    }

    public int IndexOf(string stageName)
    {
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i] != null && stages[i].StageName == stageName)
            {
                return i;
            }
        }

        return -1;
    }

    public void GoTo(string stageName)
    {
        int index = IndexOf(stageName);

        if (index < 0)
        {
            Debug.LogWarning("[Stages] No stage called '" + stageName + "'.");
            return;
        }

        GoTo(index);
    }

    public void GoTo(int index)
    {
        if (index < 0 || index >= stages.Count || stages[index] == null)
        {
            return;
        }

        // Going back (or restarting the current stage): reload the scene and start there,
        // because a stage can't undo what it already changed.
        if (CurrentIndex >= 0 && index <= CurrentIndex)
        {
            pendingStartStage = stages[index].StageName;
            Debug.Log("[Stages] Reloading the scene to start at: " + pendingStartStage);
            ReloadScene();
            return;
        }

        // Finish the running stage and fast-forward through everything in between.
        for (int i = Mathf.Max(0, CurrentIndex); i < index; i++)
        {
            if (stages[i] != null && (i > CurrentIndex || stages[i].IsRunning))
            {
                stages[i].ApplyEndState();
            }
        }

        CurrentIndex = index;
        stages[index].Begin(OnStageCompleted);
        StageChanged?.Invoke(stages[index]);
    }

    private void ReloadScene()
    {
        Scene scene = gameObject.scene;

        if (scene.buildIndex >= 0)
        {
            SceneManager.LoadScene(scene.buildIndex);
            return;
        }

#if UNITY_EDITOR
        // Scenes that aren't in Build Settings can still be reloaded in the editor by path.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.name);
#endif
    }

    public void Next()
    {
        GoTo(CurrentIndex + 1);
    }

    private void OnStageCompleted(GameStage stage)
    {
        if (stage != Current)
        {
            return;
        }

        int next = CurrentIndex + 1;

        // Stages marked "Skip Stage Always" are skipped here too.
        while (next < stages.Count - 1 && stages[next] != null && IsSkippedAlways(stages[next].StageName))
        {
            next++;
        }

        if (next < stages.Count)
        {
            GoTo(next);
        }
    }
}
