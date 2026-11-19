using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Screen-space "eyes opening" effect. A single full-screen UI Image acts as the eyelid.
/// Its bottom edge moves up in a few hesitant steps (up a bit, back down, up again...)
/// until the image has fully disappeared. While the eye is opening, the RetroLook
/// post-process blur goes down the further the eyelid is open.
/// </summary>
public class EyelidBlink : MonoBehaviour
{
    [Serializable]
    public struct EyelidStep
    {
        [Tooltip("How far the bottom edge of the eyelid has moved up. 0 = closed, 1 = fully open.")]
        [Range(0f, 1f)] public float openAmount;

        [Tooltip("Time it takes to move to this open amount.")]
        public float duration;

        [Tooltip("Pause after reaching this open amount.")]
        public float holdAfter;
    }

    [Header("Eyelid (UI Image, black)")]
    [Tooltip("Full-screen Image on a Screen Space - Overlay canvas. " +
             "Its anchors are set by this script, so the starting layout does not matter.")]
    [SerializeField] private RectTransform eyelid;

    [Header("Sequence")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private float initialDelay = 0.5f;
    [Tooltip("Played in order. The last step should have an open amount of 1.")]
    [SerializeField] private EyelidStep[] steps =
    {
        new EyelidStep { openAmount = 0.12f, duration = 0.40f, holdAfter = 0.15f },
        new EyelidStep { openAmount = 0.00f, duration = 0.30f, holdAfter = 0.40f },
        new EyelidStep { openAmount = 0.35f, duration = 0.50f, holdAfter = 0.20f },
        new EyelidStep { openAmount = 0.08f, duration = 0.35f, holdAfter = 0.30f },
        new EyelidStep { openAmount = 1.00f, duration = 0.90f, holdAfter = 0.00f },
    };
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledTime = true;    // keeps working if timeScale = 0

    [Header("Blur (RetroLook)")]
    [Tooltip("Volume whose profile contains the RetroLook override.")]
    [SerializeField] private Volume volume;
    [Tooltip("Blur radius (screen pixels) while the eyelid is closed.")]
    [SerializeField] private float maxBlur = 12f;
    [Tooltip("Maps the open amount (X, 0-1) to the blur amount (Y, 1 = max blur, 0 = sharp).")]
    [SerializeField] private AnimationCurve blurByOpenAmount = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("After")]
    [SerializeField] private bool hideEyelidWhenDone = true;

    private RetroLook retroLook;
    private Coroutine running;
    private float currentOpenAmount;

    private void Start()
    {
        // volume.profile (not sharedProfile) makes a runtime copy,
        // so the profile asset is not modified in play mode.
        if (volume != null && !volume.profile.TryGet(out retroLook))
        {
            Debug.LogWarning("EyelidBlink: No RetroLook override found on " + volume.name);
        }

        if (eyelid != null)
        {
            // Stretch to the full screen. The bottom anchor is what gets animated.
            eyelid.anchorMin = new Vector2(0f, 0f);
            eyelid.anchorMax = new Vector2(1f, 1f);
            eyelid.offsetMin = Vector2.zero;
            eyelid.offsetMax = Vector2.zero;
            eyelid.localScale = Vector3.one;
        }

        SetOpenAmount(0f);
        if (playOnStart) Play();
    }

    /// <summary>Starts the opening sequence from a closed eyelid.</summary>
    public void Play()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(OpenSequence());
    }

    private IEnumerator OpenSequence()
    {
        SetEyelidVisible(true);
        SetOpenAmount(0f);
        yield return Wait(initialDelay);

        foreach (EyelidStep step in steps)
        {
            yield return Animate(currentOpenAmount, step.openAmount, step.duration);
            yield return Wait(step.holdAfter);
        }

        if (hideEyelidWhenDone && currentOpenAmount >= 1f) SetEyelidVisible(false);
        running = null;
    }

    private IEnumerator Animate(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = ease.Evaluate(Mathf.Clamp01(t / duration));
            SetOpenAmount(Mathf.LerpUnclamped(from, to, k));
            yield return null;
        }
        SetOpenAmount(to);
    }

    private IEnumerator Wait(float seconds)
    {
        if (seconds <= 0f) yield break;
        if (useUnscaledTime) yield return new WaitForSecondsRealtime(seconds);
        else yield return new WaitForSeconds(seconds);
    }

    /// <summary>0 = fully closed (image covers the screen), 1 = fully open (image gone).</summary>
    private void SetOpenAmount(float amount)
    {
        currentOpenAmount = Mathf.Clamp01(amount);

        // Move the bottom edge of the image up; the top edge stays at the top of the screen.
        if (eyelid) eyelid.anchorMin = new Vector2(0f, currentOpenAmount);

        if (retroLook != null)
        {
            retroLook.blur.overrideState = true;
            retroLook.blur.value = maxBlur * Mathf.Clamp01(blurByOpenAmount.Evaluate(currentOpenAmount));
        }
    }

    private void SetEyelidVisible(bool visible)
    {
        if (eyelid) eyelid.gameObject.SetActive(visible);
    }
}
