using System;
using UnityEngine;
using UnityEngine.Events;

// One stage of the game (e.g. "Cutscene", "Gameplay Loop"). The StageManager runs them in order.
//
// Begin()          plays the stage.
// ApplyEndState()  puts the world into the state it would be in after the stage, without playing it
//                  (used when a stage is skipped, or when jumping past it).
//
// The lists below are applied when the stage starts, and also when it is skipped,
// so the scene ends up the same either way.
public abstract class GameStage : MonoBehaviour
{
    [Tooltip("Name shown in the Stages menu. Empty = the GameObject's name.")]
    [SerializeField] private string stageName = "";

    [Header("When this stage starts (or is skipped)")]
    [Tooltip("GameObjects switched ON.")]
    public GameObject[] activateOnEnter = new GameObject[0];

    [Tooltip("GameObjects switched OFF.")]
    public GameObject[] deactivateOnEnter = new GameObject[0];

    [Tooltip("Components enabled (e.g. CursorCameraManager for gameplay input).")]
    public Behaviour[] enableOnEnter = new Behaviour[0];

    [Tooltip("Components disabled.")]
    public Behaviour[] disableOnEnter = new Behaviour[0];

    public UnityEvent onEnter;
    public UnityEvent onComplete;

    private Action<GameStage> completedCallback;

    public string StageName
    {
        get { return string.IsNullOrEmpty(stageName) ? name : stageName; }
    }

    public bool IsRunning { get; private set; }

    public void Begin(Action<GameStage> onCompleted)
    {
        completedCallback = onCompleted;
        IsRunning = true;

        ApplyEnterLists();
        Debug.Log("[Stages] Stage started: " + StageName);
        onEnter.Invoke();

        OnBegin();
    }

    // Stops the stage if it is running and leaves the world as if it had finished.
    public void ApplyEndState()
    {
        if (!IsRunning)
        {
            ApplyEnterLists();
        }

        IsRunning = false;
        completedCallback = null;

        OnApplyEndState();
        Debug.Log("[Stages] Stage skipped: " + StageName);
    }

    // Called by the stage itself when it has finished playing.
    protected void Complete()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        Debug.Log("[Stages] Stage finished: " + StageName);
        onComplete.Invoke();

        Action<GameStage> callback = completedCallback;
        completedCallback = null;
        callback?.Invoke(this);
    }

    protected abstract void OnBegin();

    protected virtual void OnApplyEndState()
    {
    }

    private void ApplyEnterLists()
    {
        foreach (GameObject go in deactivateOnEnter)
        {
            if (go != null) go.SetActive(false);
        }

        foreach (GameObject go in activateOnEnter)
        {
            if (go != null) go.SetActive(true);
        }

        foreach (Behaviour b in disableOnEnter)
        {
            if (b != null) b.enabled = false;
        }

        foreach (Behaviour b in enableOnEnter)
        {
            if (b != null) b.enabled = true;
        }
    }
}
