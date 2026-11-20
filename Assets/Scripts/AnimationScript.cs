using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System.Collections;

// Plays open / close animations (e.g. every drawer and door of a cabinet) on one Animator.
// Every clip runs on its own layer, so several can be open at the same time: each clip only moves its own part.
// PlayAnimation(i): first call plays clip i forward (open), the next call plays it backward (close).
//
// The Animator's Animator Controller is not needed (it is ignored while playing).
public class AnimationScript : MonoBehaviour
{
    [System.Serializable]
    public class AnimationData
    {
        [Header("Animation")]
        [Tooltip("Name used to find this animation from other scripts (usually the clip name).")]
        public string animationStateName;
        public AnimationClip animationClip;

        [Header("Settings")]
        public bool canReverse = true;

        [HideInInspector]
        public bool isOpen = false;

        [HideInInspector]
        public bool isPlaying = false;

        [HideInInspector]
        public Coroutine animationCoroutine;
    }

    [Header("Animations")]
    public AnimationData[] animations;

    [Header("Animator")]
    public Animator animator;

    private PlayableGraph graph;
    private AnimationClipPlayable[] playables;

    private void Start()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            Debug.LogWarning("AnimationScript: No Animator was assigned or found.");
            return;
        }

        if (animations == null)
        {
            animations = new AnimationData[0];
        }

        // The clips are driven from here, not from the Animator Controller.
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;

        graph = PlayableGraph.Create(name + " AnimationScript");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AnimationLayerMixerPlayable mixer = AnimationLayerMixerPlayable.Create(graph, animations.Length);
        playables = new AnimationClipPlayable[animations.Length];

        for (int i = 0; i < animations.Length; i++)
        {
            AnimationData animation = animations[i];
            if (animation == null || animation.animationClip == null)
            {
                continue;
            }

            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, animation.animationClip);
            playable.SetApplyFootIK(false);
            playable.Pause(); // time is set by hand
            playable.SetTime(0);

            graph.Connect(playable, 0, mixer, i);
            mixer.SetInputWeight(i, 1f);

            playables[i] = playable;

            // Every animation starts at its first frame (closed).
            animation.isOpen = false;
            animation.isPlaying = false;
        }

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        output.SetSourcePlayable(mixer);

        graph.Play();
        graph.Evaluate();
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }

    // Index of the animation with this name (animationStateName or clip name), -1 if none.
    public int IndexOf(string animationName)
    {
        if (animations == null || string.IsNullOrEmpty(animationName))
        {
            return -1;
        }

        for (int i = 0; i < animations.Length; i++)
        {
            AnimationData a = animations[i];
            if (a == null) continue;

            if (a.animationStateName == animationName ||
                (a.animationClip != null && a.animationClip.name == animationName))
            {
                return i;
            }
        }

        return -1;
    }

    public void PlayAnimation(int animationIndex)
    {
        if (animations == null ||
            animationIndex < 0 ||
            animationIndex >= animations.Length)
        {
            Debug.LogWarning("AnimationScript: Invalid animation index.");
            return;
        }

        AnimationData animation = animations[animationIndex];

        if (animation == null || animation.isPlaying)
        {
            return;
        }

        if (playables == null || !playables[animationIndex].IsValid())
        {
            Debug.LogWarning("AnimationScript: Animation " + animationIndex + " has no clip (or no Animator).");
            return;
        }

        // First call = forward (open). Next call = backward (close).
        if (!animation.isOpen)
        {
            animation.animationCoroutine = StartCoroutine(Play(animationIndex, true));
        }
        else if (animation.canReverse)
        {
            animation.animationCoroutine = StartCoroutine(Play(animationIndex, false));
        }
    }

    private IEnumerator Play(int index, bool forward)
    {
        AnimationData animation = animations[index];
        AnimationClipPlayable playable = playables[index];

        animation.isPlaying = true;

        float duration = animation.animationClip.length;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            playable.SetTime((forward ? t : 1f - t) * duration);

            yield return null;
        }

        playable.SetTime(forward ? duration : 0f);

        animation.isOpen = forward;
        animation.isPlaying = false;
        animation.animationCoroutine = null;
    }

    public bool IsAnimationPlaying(int animationIndex)
    {
        if (animations == null ||
            animationIndex < 0 ||
            animationIndex >= animations.Length)
        {
            return false;
        }

        return animations[animationIndex].isPlaying;
    }

    public bool IsAnimationOpen(int animationIndex)
    {
        if (animations == null ||
            animationIndex < 0 ||
            animationIndex >= animations.Length)
        {
            return false;
        }

        return animations[animationIndex].isOpen;
    }
}
