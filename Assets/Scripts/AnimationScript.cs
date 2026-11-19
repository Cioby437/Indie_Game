using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AnimationScript : MonoBehaviour
{
    [System.Serializable]
    public class AnimationData
    {
        [Header("Animation")]
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

    private void Start()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            Debug.LogWarning(
                "AnimationScript: No Animator was assigned or found."
            );

            return;
        }

        // Put every animation at its starting position.
        foreach (AnimationData animation in animations)
        {
            if (animation == null ||
                animation.animationClip == null ||
                string.IsNullOrEmpty(animation.animationStateName))
            {
                continue;
            }

            animation.isOpen = false;
            animation.isPlaying = false;

            animator.speed = 0f;

            animator.Play(
                animation.animationStateName,
                0,
                0f
            );

            animator.Update(0f);
        }
    }

    public void PlayAnimation(int animationIndex)
    {
        if (animations == null ||
            animationIndex < 0 ||
            animationIndex >= animations.Length)
        {
            Debug.LogWarning(
                "AnimationScript: Invalid animation index."
            );

            return;
        }

        AnimationData animation = animations[animationIndex];

        if (animation == null)
        {
            return;
        }

        if (animation.isPlaying)
        {
            return;
        }

        if (animator == null)
        {
            Debug.LogWarning(
                "AnimationScript: Animator is not assigned."
            );

            return;
        }

        if (animation.animationClip == null)
        {
            Debug.LogWarning(
                "AnimationScript: Animation Clip is not assigned."
            );

            return;
        }

        if (string.IsNullOrEmpty(animation.animationStateName))
        {
            Debug.LogWarning(
                "AnimationScript: Animation State Name is not assigned."
            );

            return;
        }

        // Stop any existing animation.
        if (animation.animationCoroutine != null)
        {
            StopCoroutine(animation.animationCoroutine);
        }

        // First click = forward.
        // Second click = reverse.
        if (!animation.isOpen)
        {
            animation.animationCoroutine =
                StartCoroutine(
                    PlayForward(animation)
                );
        }
        else if (animation.canReverse)
        {
            animation.animationCoroutine =
                StartCoroutine(
                    PlayBackward(animation)
                );
        }
    }

    private IEnumerator PlayForward(AnimationData animation)
    {
        animation.isPlaying = true;

        animator.speed = 0f;

        float elapsed = 0f;
        float duration = animation.animationClip.length;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float normalizedTime =
                Mathf.Clamp01(elapsed / duration);

            animator.Play(
                animation.animationStateName,
                0,
                normalizedTime
            );

            animator.Update(0f);

            yield return null;
        }

        // Make absolutely sure we end at frame 18 / 100%.
        animator.Play(
            animation.animationStateName,
            0,
            1f
        );

        animator.Update(0f);

        animator.speed = 0f;

        animation.isOpen = true;
        animation.isPlaying = false;
        animation.animationCoroutine = null;
    }

    private IEnumerator PlayBackward(AnimationData animation)
    {
        animation.isPlaying = true;

        animator.speed = 0f;

        float elapsed = 0f;
        float duration = animation.animationClip.length;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float normalizedTime =
                Mathf.Clamp01(
                    1f - (elapsed / duration)
                );

            animator.Play(
                animation.animationStateName,
                0,
                normalizedTime
            );

            animator.Update(0f);

            yield return null;
        }

        // Return to frame 0.
        animator.Play(
            animation.animationStateName,
            0,
            0f
        );

        animator.Update(0f);

        animator.speed = 0f;

        animation.isOpen = false;
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

    private void OnDestroy()
    {
        if (animations == null)
        {
            return;
        }

        foreach (AnimationData animation in animations)
        {
            if (animation != null &&
                animation.animationCoroutine != null)
            {
                StopCoroutine(animation.animationCoroutine);
            }
        }
    }
}