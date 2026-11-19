using System.Collections;
using UnityEngine;

/// <summary>
/// Cycles a character through four Mixamo clips so it looks confused and
/// is searching for something in its pocket.
///
/// The Animator Controller only needs the four states (no transitions).
/// This script crossfades between them with CrossFadeInFixedTime.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ConfusedCharacterAnimator : MonoBehaviour
{

    [Tooltip("Used as the clip length when no clip is assigned.")]
    [Min(0.1f)] public float fallbackClipLength = 4f;

    [Header("Behaviour")]
    [Tooltip("Move the character starts with.")]
    public Move firstMove = Move.Looking;

    [Header("Moves (state names must match the Animator Controller)")]
    public MoveSettings looking = new MoveSettings
    {
        stateName = "Looking",
        minLoops = 1,
        maxLoops = 2
    };

    public MoveSettings lookingAround = new MoveSettings
    {
        stateName = "Looking Around",
        minLoops = 1,
        maxLoops = 2
    };

    public MoveSettings lookingDown = new MoveSettings
    {
        stateName = "Looking Down",
        minLoops = 1,
        maxLoops = 1
    };

    public MoveSettings searchingPockets = new MoveSettings
    {
        stateName = "Unreal_Take_SearchingPockets",
        minLoops = 1,
        maxLoops = 3
    };

    [Tooltip("Random playback speed variation, so repeats don't look identical (0.08 = +/-8%).")]
    [Range(0f, 0.3f)] public float speedVariation = 0.08f;

    [Tooltip("Seconds to wait before the first move.")]
    [Min(0f)] public float startDelay = 0.5f;

    // Rows = current move, columns = next move.
    // Order: Looking, LookingAround, LookingDown, SearchingPocket.
    // A weight of 0 means "never go there directly".
    //
    //   Looking         -> mostly glances around or down, sometimes searches
    //   LookingAround   -> often looks down next, sometimes searches or stops to look
    //   LookingDown     -> usually leads into searching the pocket
    //   SearchingPocket -> doesn't find it: looks around again or looks down
    private static readonly float[,] Weights =
    {
        //  Look  Around  Down  Pocket
        {   0f,   3f,     3f,   1f },   // from Looking
        {   2f,   0f,     3f,   2f },   // from LookingAround
        {   1f,   1f,     0f,   4f },   // from LookingDown
        {   1f,   3f,     2f,   0f }    // from SearchingPocket
    };

    private Animator animator;
    private Coroutine cycleRoutine;
    public enum Move
    {
        Looking,
        LookingAround,
        LookingDown,
        SearchingPocket
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private IEnumerator CycleRoutine()
    {
        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        Move current = firstMove;

        while (true)
        {
            MoveSettings settings = GetSettings(current);

            // Slight speed change per move so repeats feel less mechanical.
            animator.speed = 1f + Random.Range(-speedVariation, speedVariation);

            animator.CrossFadeInFixedTime(
                settings.stateName,
                settings.blendTime
            );

            yield return new WaitForSeconds(GetDuration(settings));

            current = PickNext(current);
        }
    }

    private float GetDuration(MoveSettings settings)
    {
        int minLoops = Mathf.Max(1, settings.minLoops);
        int maxLoops = Mathf.Max(minLoops, settings.maxLoops);

        int loops = Random.Range(minLoops, maxLoops + 1);

        float length = settings.clip != null
            ? settings.clip.length
            : fallbackClipLength;

        // Animator speed stretches or shrinks the playback time.
        return length * loops / Mathf.Max(0.01f, animator.speed);
    }

    private MoveSettings GetSettings(Move move)
    {
        switch (move)
        {
            case Move.LookingAround: return lookingAround;
            case Move.LookingDown: return lookingDown;
            case Move.SearchingPocket: return searchingPockets;
            default: return looking;
        }
    }

    private void OnDisable()
    {
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }

        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    private void OnEnable()
    {
        ValidateStates();

        cycleRoutine = StartCoroutine(CycleRoutine());
    }

    private Move PickNext(Move from)
    {
        int row = (int)from;

        float total = 0f;

        for (int i = 0; i < 4; i++)
        {
            total += Weights[row, i];
        }

        float roll = Random.value * total;

        for (int i = 0; i < 4; i++)
        {
            float weight = Weights[row, i];

            if (weight <= 0f)
            {
                continue;
            }

            roll -= weight;

            if (roll <= 0f)
            {
                return (Move)i;
            }
        }

        return Move.Looking;
    }

    // Warns if a state name doesn't exist in the controller,
    // which is the most common reason for "nothing plays".
    private void ValidateStates()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                "ConfusedCharacterAnimator: No Animator Controller assigned.",
                this
            );

            return;
        }

        string layerName = animator.GetLayerName(0);

        foreach (Move move in System.Enum.GetValues(typeof(Move)))
        {
            string stateName = GetSettings(move).stateName;

            int shortHash = Animator.StringToHash(stateName);
            int fullHash = Animator.StringToHash(layerName + "." + stateName);

            if (!animator.HasState(0, shortHash) &&
                !animator.HasState(0, fullHash))
            {
                Debug.LogWarning(
                    "ConfusedCharacterAnimator: State '" + stateName +
                    "' was not found in the Animator Controller.",
                    this
                );
            }
        }
    }

    [System.Serializable]
    public class MoveSettings
    {

        [Tooltip("Crossfade time (seconds) when entering this state.")]
        [Min(0f)] public float blendTime = 0.3f;

        [Tooltip("The clip used by that state. Only needed to read its length.")]
        public AnimationClip clip;
        [Min(1)] public int maxLoops = 2;

        [Tooltip("How many times the clip plays before the next move (random between min and max).")]
        [Min(1)] public int minLoops = 1;
        [Tooltip("Exact state name in the Animator Controller (case sensitive).")]
        public string stateName;
    }
}