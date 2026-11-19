using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Anim_SubInteractive1_Interactive3 : MonoBehaviour
{
    private static readonly int RummagingHash = Animator.StringToHash("Rummaging");
    private const string SurprisedState = "Surprised";
    private const string RunningTurnState = "Running Turn 180";
    private const string RunningState = "Running";

    [SerializeField] private Animator animator;
    [SerializeField] private float crossFadeDuration = 0.25f;
    [Tooltip("Seconds after pressing Space before the object is disabled. 0 = never disable.")]
    [SerializeField] private float disableDelay = 0f;

    private bool triggered;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (triggered) return;

        KeepRummaging();

        if (Keyboard.current == null) return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            triggered = true;

            if (animator != null)
                StartCoroutine(PlaySequence());

            if (disableDelay > 0f)
                StartCoroutine(DisableAfterDelay());
        }
    }

    // Restarts Rummaging when it reaches its end, and pulls the animator back
    // to it if a controller transition tries to leave it before Space is pressed.
    private void KeepRummaging()
    {
        if (animator == null) return;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        bool inRummaging = info.shortNameHash == RummagingHash;

        if (!inRummaging || animator.IsInTransition(0))
        {
            animator.Play(RummagingHash, 0, inRummaging ? info.normalizedTime % 1f : 0f);
            return;
        }

        if (info.normalizedTime >= 1f)
            animator.Play(RummagingHash, 0, info.normalizedTime % 1f);
    }

    private IEnumerator PlaySequence()
    {
        yield return PlayToEnd(SurprisedState);
        yield return PlayToEnd(RunningTurnState);
        animator.CrossFadeInFixedTime(RunningState, crossFadeDuration);
    }

    // Fades into the state, then waits until it is close enough to its end
    // that the next cross-fade finishes exactly as this clip does.
    private IEnumerator PlayToEnd(string stateName)
    {
        animator.CrossFadeInFixedTime(stateName, crossFadeDuration);

        int stateHash = Animator.StringToHash(stateName);

        while (true)
        {
            yield return null;

            if (animator.IsInTransition(0))
                continue;

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);

            if (info.shortNameHash != stateHash)
                continue;

            float fadeFraction = info.length > 0f ? crossFadeDuration / info.length : 0f;

            if (info.normalizedTime >= 1f - fadeFraction)
                yield break;
        }
    }

    private IEnumerator DisableAfterDelay()
    {
        yield return new WaitForSeconds(disableDelay);
        gameObject.SetActive(false);
    }
}
