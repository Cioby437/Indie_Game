using FMODUnity;
using UnityEngine;

/// One "hidden sound" puzzle: the notch frequency / pitch that reveals the hidden layer.
/// This is only a data asset (no audio). The sound itself lives in FMOD:
///   Option A (recommended): a track inside Scriptable_1 whose volume is automated by "Reveal".
///                           Leave Hidden Event empty and set Reveal Parameter to "Reveal".
///   Option B: a separate FMOD event, assigned to Hidden Event.
[CreateAssetMenu(menuName = "Audio/Secret Layer Puzzle")]
public class SecretLayerPuzzle : ScriptableObject
{
    [Tooltip("Partial reveal over this extra distance (radio-tuning effect). 0 = on/off.")]
    public float frequencyFadeOctaves = 0.5f;
    [Tooltip("Full reveal within this many octaves of the target (0.1 = about one semitone and a half).")]
    public float frequencyToleranceOctaves = 0.1f;

    [Tooltip("Option B only. Leave empty when the hidden layer is a track inside Scriptable_1.")]
    public EventReference hiddenEvent;

    [Header("Solving")]
    [Tooltip("How long the player has to hold the exact match to solve it.")]
    public float holdToSolveSeconds = 1.5f;

    [Tooltip("On: the TV's FREQUENCY and PITCH text must read exactly the targets (e.g. 1000 and 5). " +
             "Off: the tolerance / fade settings below are used.")]
    public bool exactMatch = true;

    [Tooltip("Smoothing: seconds the hidden event's Reveal parameter takes to go all the way from 0 to 1. " +
             "0 = use the TV's Reveal Fade Speed.")]
    public float revealSeconds = 2f;

    [Header("Getting Closer (Exact Match)")]
    [Tooltip("The hidden sound starts to be heard within this many Hz (as shown on the TV) of the target frequency.")]
    public float frequencyHearingRangeHz = 3000f;

    [Tooltip("The hidden sound starts to be heard within this much pitch (as shown on the TV) of the target pitch.")]
    public float pitchHearingRange = 8f;

    [Tooltip("1 = volume grows evenly as the player gets closer. Higher = quiet until quite close, then rises fast.")]
    public float closenessCurve = 1.5f;

    [Tooltip("Reveal value just before the exact spot (the last bit up to 1 only comes on the exact readings).")]
    [Range(0f, 1f)] public float maxRevealNearTarget = 0.85f;

    [Tooltip("All = frequency AND pitch must match. Any = either one is enough.")]
    public MatchMode matchMode = MatchMode.All;
    public float pitchFadeSemitones = 2f;
    public float pitchToleranceSemitones = 0.5f;

    [Tooltip("FMOD parameter (0-1) set as the player gets closer to the target. " +
             "Option A: a parameter on Scriptable_1 that automates the hidden track's volume. " +
             "Option B: a parameter on the hidden event (empty = the event's volume is used).")]
    public string revealParameter = "Reveal";
    public bool stayRevealedWhenSolved = true;
    public float targetFrequencyHz = 1000f;
    public float targetPitchSemitones = -5f;

    [Header("Notch frequency")]
    public bool useFrequency = true;

    [Header("Pitch")]
    public bool usePitch = true;
    public enum MatchMode { All, Any }

    /// 0 = nothing heard, 1 = fully matched.
    public float Evaluate(float notchHz, float pitchSemitones)
    {
        if (!useFrequency && !usePitch) return 0f;

        if (exactMatch)
        {
            // The TV passes the values exactly as they read on screen (already rounded).
            if (IsExactHit(notchHz, pitchSemitones))
            {
                return 1f;
            }

            // Not there yet: louder the closer the readings are.
            return Mathf.Pow(Closeness(notchHz, pitchSemitones), Mathf.Max(0.01f, closenessCurve)) * maxRevealNearTarget;
        }

        float f = useFrequency
            ? Score(Mathf.Abs(Mathf.Log(notchHz / targetFrequencyHz, 2f)), frequencyToleranceOctaves, frequencyFadeOctaves)
            : (matchMode == MatchMode.All ? 1f : 0f);
        float p = usePitch
            ? Score(Mathf.Abs(pitchSemitones - targetPitchSemitones), pitchToleranceSemitones, pitchFadeSemitones)
            : (matchMode == MatchMode.All ? 1f : 0f);

        return matchMode == MatchMode.All ? Mathf.Min(f, p) : Mathf.Max(f, p);
    }

    [Header("Screen static hint")]
    [Tooltip("The TV screen starts to get static this many octaves away from the target frequency.")]
    public float frequencyHintOctaves = 3f;
    [Tooltip("The TV screen starts to get static this many semitones away from the target pitch.")]
    public float pitchHintSemitones = 8f;

    /// 0 = far away, 1 = on target. Wider than Evaluate(), used for the screen static.
    /// With All, each axis counts for half, so the player can feel progress on one axis
    /// but only gets the full static when both are right.
    public float Proximity(float notchHz, float pitchSemitones)
    {
        if (!useFrequency && !usePitch) return 0f;

        if (exactMatch)
        {
            return IsExactHit(notchHz, pitchSemitones) ? 1f : Closeness(notchHz, pitchSemitones);
        }

        float f = useFrequency
            ? Hint(Mathf.Abs(Mathf.Log(notchHz / targetFrequencyHz, 2f)), frequencyToleranceOctaves, frequencyHintOctaves)
            : 1f;
        float p = usePitch
            ? Hint(Mathf.Abs(pitchSemitones - targetPitchSemitones), pitchToleranceSemitones, pitchHintSemitones)
            : 1f;

        if (matchMode == MatchMode.Any)
        {
            return Mathf.Max(useFrequency ? f : 0f, usePitch ? p : 0f);
        }

        return (f + p) * 0.5f;
    }

    /// True when the TV's readings are exactly the targets.
    public bool IsExactHit(float notchHz, float pitchSemitones)
    {
        bool fHit = Mathf.Abs(notchHz - targetFrequencyHz) < 0.5f;
        bool pHit = Mathf.Abs(pitchSemitones - targetPitchSemitones) < 0.01f;

        return matchMode == MatchMode.All
            ? (!useFrequency || fHit) && (!usePitch || pHit)
            : (useFrequency && fHit) || (usePitch && pHit);
    }

    /// 0 = outside the hearing range, 1 = on the target. Distance on the TV's graph, in its own units.
    public float Closeness(float notchHz, float pitchSemitones)
    {
        float f = useFrequency && frequencyHearingRangeHz > 0f
            ? Mathf.Abs(notchHz - targetFrequencyHz) / frequencyHearingRangeHz
            : 0f;
        float p = usePitch && pitchHearingRange > 0f
            ? Mathf.Abs(pitchSemitones - targetPitchSemitones) / pitchHearingRange
            : 0f;

        float distance = matchMode == MatchMode.All
            ? Mathf.Sqrt(f * f + p * p)            // both count
            : Mathf.Min(useFrequency ? f : 1f, usePitch ? p : 1f); // the closer one counts

        return 1f - Mathf.Clamp01(distance);
    }

    static float Hint(float distance, float tolerance, float range)
    {
        if (distance <= tolerance) return 1f;
        if (range <= tolerance) return 0f;
        float t = 1f - Mathf.Clamp01((distance - tolerance) / (range - tolerance));
        return t * t * (3f - 2f * t); // smoothstep
    }

    static float Score(float distance, float tolerance, float fade)
    {
        if (distance <= tolerance) return 1f;
        if (fade <= 0f) return 0f;
        return 1f - Mathf.Clamp01((distance - tolerance) / fade);
    }
}