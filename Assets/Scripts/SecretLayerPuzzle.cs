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

        float f = useFrequency
            ? Score(Mathf.Abs(Mathf.Log(notchHz / targetFrequencyHz, 2f)), frequencyToleranceOctaves, frequencyFadeOctaves)
            : (matchMode == MatchMode.All ? 1f : 0f);
        float p = usePitch
            ? Score(Mathf.Abs(pitchSemitones - targetPitchSemitones), pitchToleranceSemitones, pitchFadeSemitones)
            : (matchMode == MatchMode.All ? 1f : 0f);

        return matchMode == MatchMode.All ? Mathf.Min(f, p) : Mathf.Max(f, p);
    }

    static float Score(float distance, float tolerance, float fade)
    {
        if (distance <= tolerance) return 1f;
        if (fade <= 0f) return 0f;
        return 1f - Mathf.Clamp01((distance - tolerance) / fade);
    }
}