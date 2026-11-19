using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Labels a field inside an array element with that element's 1-based index,
// e.g. "Sub-Interactive_Interactive{0}" -> "Sub-Interactive_Interactive3".
public class IndexedLabelAttribute : PropertyAttribute
{
    public readonly string format;

    public IndexedLabelAttribute(string format)
    {
        this.format = format;
    }
}

public class ScenarioManager : MonoBehaviour
{

    [Tooltip("Index of the Tier 1 Sound whose sub scenarios are picked from (0 = Tier 1 Sound_1).")]
    [Range(0, Tier1SoundCount - 1)]
    public int activeTier1SoundIndex = 0;

    public CursorCameraManager cursorCameraManager;

    [Header("Testing")]
    [Tooltip("If true, scenarios can start at any in-game time, not only between 00:00 and 01:00 am.")]
    public bool overrideConditions = true;

    [Header("Tier 1 Sounds")]
    public Tier1Sound[] tier1Sounds = new Tier1Sound[Tier1SoundCount];

    [Header("Clock")]
    [Tooltip("Object with the TMPro text showing the in-game time, e.g. \"Time: 00:00 am\".")]
    public GameObject timeTextObject;
    private const int SubScenarioCount = 4;
    private const int Tier1SoundCount = 4;

    private static readonly System.Text.RegularExpressions.Regex TimePattern =
        new System.Text.RegularExpressions.Regex(
            @"(\d{1,2}):(\d{2})\s*(am|pm)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

    private readonly HashSet<Scenario> playedScenarios = new HashSet<Scenario>();

    private Scenario lastScenario;
    private Coroutine randomReplayRoutine;
    private bool scenarioStarted = false;
    private EventInstance segmentInstance;

    private TMP_Text timeText;

    private IEnumerable<Scenario> AllScenarios()
    {
        if (tier1Sounds == null)
        {
            yield break;
        }

        foreach (Tier1Sound tier1Sound in tier1Sounds)
        {
            if (tier1Sound == null || tier1Sound.subScenarios == null)
            {
                continue;
            }

            foreach (Scenario scenario in tier1Sound.subScenarios)
            {
                if (scenario != null)
                {
                    yield return scenario;
                }
            }
        }
    }

    private void DisableAllScenarioObjects()
    {
        foreach (Scenario scenario in AllScenarios())
        {
            if (scenario.subInteractiveObject != null)
                scenario.subInteractiveObject.SetActive(false);

            if (scenario.textObject != null)
                scenario.textObject.SetActive(false);

            if (scenario.button != null)
                scenario.button.gameObject.SetActive(false);

            if (scenario.preCountdownObject != null)
                scenario.preCountdownObject.SetActive(false);

            if (scenario.blackScreenPanel != null)
            {
                SetPanelAlpha(scenario.blackScreenPanel, 0f);
                scenario.blackScreenPanel.gameObject.SetActive(false);
            }

            if (scenario.audioEmitterObject != null)
                scenario.audioEmitterObject.SetActive(false);
        }
    }

    private IEnumerator FadePanel(
        Image panel,
        float from,
        float to,
        float duration
    )
    {
        if (panel == null)
        {
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            SetPanelAlpha(
                panel,
                Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration))
            );

            yield return null;
        }

        SetPanelAlpha(panel, to);
    }

    // Length of the emitter's FMOD event in seconds (0 if unknown).
    private float GetEmitterLength(StudioEventEmitter emitter)
    {
        if (emitter == null || emitter.EventReference.IsNull)
        {
            return 0f;
        }

        EventDescription description =
            RuntimeManager.GetEventDescription(emitter.EventReference);

        if (!description.isValid())
        {
            return 0f;
        }

        description.getLength(out int lengthMs);

        return lengthMs / 1000f;
    }

    // True between 00:00 am and 01:00 am, read from the clock's TMPro text.
    private bool IsWithinActiveHours()
    {
        if (timeText == null)
        {
            Debug.LogWarning(
                "ScenarioManager: No TMPro time text assigned."
            );

            return false;
        }

        var match = TimePattern.Match(timeText.text);

        if (!match.Success)
        {
            return false;
        }

        int hours = int.Parse(match.Groups[1].Value);
        bool isAm = match.Groups[3].Value.ToLower() == "am";

        // 12:xx am is the same as 00:xx am on a 12-hour clock.
        return isAm && (hours == 0 || hours == 12);
    }

    // Every bed click starts the next scenario
    // (ignored while one is already running).
    private void OnBedClicked()
    {
        TryStartScenario();
    }

    private void OnDisable()
    {
        if (cursorCameraManager != null)
        {
            cursorCameraManager.BedClicked -= OnBedClicked;
        }

        StopRandomReplays();
    }

    private void OnEnable()
    {
        if (cursorCameraManager != null)
        {
            cursorCameraManager.BedClicked += OnBedClicked;
        }
    }

    private void OnValidate()
    {
        if (tier1Sounds == null || tier1Sounds.Length != Tier1SoundCount)
        {
            System.Array.Resize(ref tier1Sounds, Tier1SoundCount);
        }

        foreach (Tier1Sound tier1Sound in tier1Sounds)
        {
            if (tier1Sound != null &&
                (tier1Sound.subScenarios == null ||
                 tier1Sound.subScenarios.Length != SubScenarioCount))
            {
                System.Array.Resize(ref tier1Sound.subScenarios, SubScenarioCount);
            }
        }
    }

    // Plays the event from startSeconds and stops it the moment the loop region
    // it starts in reaches its end (detected as the timeline jumping backwards).
    private IEnumerator PlayLoopSegment(
        EventReference eventReference,
        Transform source,
        float startSeconds
    )
    {
        int startMs = Mathf.RoundToInt(startSeconds * 1000f);

        EventInstance instance = RuntimeManager.CreateInstance(eventReference);
        segmentInstance = instance;

        instance.set3DAttributes(RuntimeUtils.To3DAttributes(source));

        FMOD_Spatializer spatializer = source.GetComponent<FMOD_Spatializer>();
        if (spatializer != null)
        {
            spatializer.ApplyTo(instance);
        }

        instance.start();
        // Queued right after start(), so the seek is applied before anything is heard.
        instance.setTimelinePosition(startMs);

        bool armed = false;
        int lastPosition = 0;

        while (true)
        {
            yield return null;

            if (!instance.isValid())
            {
                break;
            }

            instance.getPlaybackState(out PLAYBACK_STATE state);

            if (state == PLAYBACK_STATE.STOPPED)
            {
                break;
            }

            instance.set3DAttributes(RuntimeUtils.To3DAttributes(source));

            if (spatializer != null)
            {
                spatializer.ApplyTo(instance);
            }

            instance.getTimelinePosition(out int position);

            // Wait until the seek to the start marker has actually taken effect.
            if (!armed)
            {
                if (state == PLAYBACK_STATE.PLAYING && position >= startMs)
                {
                    armed = true;
                    lastPosition = position;
                }

                continue;
            }

            if (position < lastPosition)
            {
                break;
            }

            lastPosition = position;
        }

        StopSegmentInstance();
    }

    private IEnumerator RandomReplayLoop(
        Scenario scenario,
        EventReference eventReference
    )
    {
        while (true)
        {
            yield return new WaitForSeconds(
                Random.Range(
                    scenario.randomReplayMinDelay,
                    scenario.randomReplayMaxDelay
                )
            );

            AudioStartPoint startPoint = scenario.audioStartPoints[
                Random.Range(0, scenario.audioStartPoints.Count)
            ];

            yield return StartCoroutine(
                PlayLoopSegment(
                    eventReference,
                    scenario.audioEmitterObject.transform,
                    startPoint.positionSeconds
                )
            );
        }
    }

    private IEnumerator RunScenario(Scenario scenario)
    {
        if (scenario.subInteractiveObject != null)
        {
            scenario.subInteractiveObject.SetActive(true);
        }

        // =========================================================
        // STATE + LISTENERS
        // =========================================================

        bool textHidden = false;        // true while the player is away from the bed
        bool buttonPhaseActive = false; // true once the typewriter has finished and we're waiting for the button

        // Window button pressed: hide the text and the scenario button.
        UnityEngine.Events.UnityAction hideAction = () =>
        {
            textHidden = true;

            if (scenario.textObject != null)
            {
                scenario.textObject.SetActive(false);
            }

            if (scenario.button != null)
            {
                scenario.button.gameObject.SetActive(false);
            }
        };

        // Bed clicked again: show the text, and the button only if it's due to be visible.
        System.Action showAction = () =>
        {
            textHidden = false;

            if (scenario.textObject != null)
            {
                scenario.textObject.SetActive(true);
            }

            if (buttonPhaseActive && scenario.button != null)
            {
                scenario.button.gameObject.SetActive(true);
            }
        };

        if (cursorCameraManager != null)
        {
            if (cursorCameraManager.windowButton != null)
            {
                cursorCameraManager.windowButton.onClick.AddListener(hideAction);
            }

            cursorCameraManager.BedClicked += showAction;
        }

        // =========================================================
        // STEP 1 - TYPEWRITER TEXT
        // =========================================================

        if (scenario.textObject != null)
        {
            scenario.textObject.SetActive(true);
        }

        if (scenario.text != null)
        {
            yield return StartCoroutine(
                TypeText(
                    scenario.text,
                    scenario.characterDelay
                )
            );
        }

        // =========================================================
        // STEP 2 - BUTTON
        // =========================================================

        if (scenario.button != null)
        {
            buttonPhaseActive = true;

            // If the player left while the text was still typing,
            // keep the button hidden until they come back.
            scenario.button.gameObject.SetActive(!textHidden);

            bool buttonPressed = false;

            UnityEngine.Events.UnityAction buttonAction =
                () => buttonPressed = true;

            scenario.button.onClick.AddListener(buttonAction);

            yield return new WaitUntil(
                () => buttonPressed
            );

            scenario.button.onClick.RemoveListener(
                buttonAction
            );

            buttonPhaseActive = false;

            scenario.button.gameObject.SetActive(false);
        }

        // Text phase is over, so stop listening.
        if (cursorCameraManager != null)
        {
            if (cursorCameraManager.windowButton != null)
            {
                cursorCameraManager.windowButton.onClick.RemoveListener(hideAction);
            }

            cursorCameraManager.BedClicked -= showAction;
        }

        if (scenario.textObject != null)
        {
            scenario.textObject.SetActive(false);
        }

        // =========================================================
        // STEP 3 - PANEL FADE IN (starts on button click)
        //          + 3 → 1 COUNTDOWN (runs while the panel fades)
        // =========================================================

        Coroutine fadeIn = null;

        if (scenario.blackScreenPanel != null)
        {
            SetPanelAlpha(scenario.blackScreenPanel, 0f);
            scenario.blackScreenPanel.gameObject.SetActive(true);

            fadeIn = StartCoroutine(
                FadePanel(
                    scenario.blackScreenPanel,
                    0f,
                    1f,
                    scenario.fadeInDuration
                )
            );
        }

        if (scenario.preCountdownObject != null)
        {
            scenario.preCountdownObject.SetActive(true);
        }

        for (int i = 3; i >= 1; i--)
        {
            if (scenario.preCountdownText != null)
            {
                scenario.preCountdownText.text =
                    i.ToString();
            }

            yield return new WaitForSeconds(
                scenario.countdownInterval
            );
        }

        if (scenario.preCountdownObject != null)
        {
            scenario.preCountdownObject.SetActive(false);
        }

        // Make sure the panel has reached full alpha before the audio starts.
        if (fadeIn != null)
        {
            yield return fadeIn;
        }

        // =========================================================
        // STEP 4 - AUDIO (FMOD emitter object)
        // =========================================================

        if (scenario.audioEmitterObject != null &&
            scenario.audioStartPoints != null &&
            scenario.audioStartPoints.Count > 0)
        {
            StudioEventEmitter emitter =
                scenario.audioEmitterObject.GetComponent<StudioEventEmitter>();

            if (emitter != null && !emitter.EventReference.IsNull)
            {
                // The black screen lasts exactly as long as the first loop region.
                yield return StartCoroutine(
                    PlayLoopSegment(
                        emitter.EventReference,
                        scenario.audioEmitterObject.transform,
                        scenario.audioStartPoints[0].positionSeconds
                    )
                );

                randomReplayRoutine = StartCoroutine(
                    RandomReplayLoop(scenario, emitter.EventReference)
                );
            }
            else
            {
                Debug.LogWarning(
                    "ScenarioManager: The audio object has no StudioEventEmitter with an event assigned."
                );

                yield return new WaitForSeconds(1f);
            }
        }
        else if (scenario.audioEmitterObject != null)
        {
            StudioEventEmitter emitter =
                scenario.audioEmitterObject.GetComponent<StudioEventEmitter>();

            // Read the event length before enabling the object.
            float audioLength = GetEmitterLength(emitter);

            // Enabling the object makes the emitter play (Play On: Object Enable).
            scenario.audioEmitterObject.SetActive(true);

            if (audioLength > 0f)
            {
                yield return new WaitForSeconds(audioLength);
            }
            else if (emitter != null)
            {
                // Length unknown (e.g. looping / sustained event):
                // wait for the emitter to stop playing on its own.
                yield return null;

                while (emitter.IsPlaying())
                {
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning(
                    "ScenarioManager: The audio object has no StudioEventEmitter component."
                );

                yield return new WaitForSeconds(1f);
            }

            // Disabling the object stops the emitter (Stop On: Object Disable)
            // and lets it play again the next time it is enabled.
            scenario.audioEmitterObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning(
                "ScenarioManager: No audio emitter object assigned to the current scenario."
            );

            yield return new WaitForSeconds(1f);
        }

        // =========================================================
        // STEP 4b - PANEL FADE OUT (after the audio is over)
        // =========================================================

        if (scenario.blackScreenPanel != null)
        {
            yield return StartCoroutine(
                FadePanel(
                    scenario.blackScreenPanel,
                    1f,
                    0f,
                    scenario.fadeOutDuration
                )
            );

            // Turn the panel off so it can't block the back button or any other UI.
            scenario.blackScreenPanel.gameObject.SetActive(false);
        }

        if (scenario.subInteractiveObject != null)
        {
            scenario.subInteractiveObject.SetActive(false);
        }

        // =========================================================
        // SCENARIO COMPLETE
        // =========================================================

        StopRandomReplays();

        scenarioStarted = false;
    }

    private void SetPanelAlpha(Image panel, float alpha)
    {
        Color color = panel.color;
        color.a = alpha;
        panel.color = color;
    }

    private void Start()
    {
        if (timeTextObject != null)
        {
            timeText = timeTextObject.GetComponent<TMP_Text>();
        }

        DisableAllScenarioObjects();
    }

    // Picks a random unplayed Sub Scenario from the active Tier 1 Sound only.
    private void StartNextScenario()
    {
        if (tier1Sounds == null ||
            activeTier1SoundIndex < 0 ||
            activeTier1SoundIndex >= tier1Sounds.Length ||
            tier1Sounds[activeTier1SoundIndex] == null ||
            tier1Sounds[activeTier1SoundIndex].subScenarios == null)
        {
            Debug.LogWarning(
                "ScenarioManager: The active Tier 1 Sound has no sub scenarios."
            );

            return;
        }

        Tier1Sound chosenSound = tier1Sounds[activeTier1SoundIndex];

        List<Scenario> unplayed = new List<Scenario>();

        foreach (Scenario scenario in chosenSound.subScenarios)
        {
            if (scenario != null && !playedScenarios.Contains(scenario))
            {
                unplayed.Add(scenario);
            }
        }

        // All of them have played: start a new round, but don't repeat
        // the scenario that just finished straight away.
        if (unplayed.Count == 0)
        {
            foreach (Scenario scenario in chosenSound.subScenarios)
            {
                if (scenario != null)
                {
                    playedScenarios.Remove(scenario);

                    if (scenario != lastScenario)
                    {
                        unplayed.Add(scenario);
                    }
                }
            }

            if (unplayed.Count == 0 && lastScenario != null)
            {
                unplayed.Add(lastScenario);
            }

            if (unplayed.Count == 0)
            {
                Debug.LogWarning(
                    "ScenarioManager: " + chosenSound.name + " has no sub scenarios assigned."
                );

                return;
            }
        }

        Scenario chosenScenario = unplayed[Random.Range(0, unplayed.Count)];

        playedScenarios.Add(chosenScenario);
        lastScenario = chosenScenario;
        scenarioStarted = true;

        StartCoroutine(RunScenario(chosenScenario));
    }

    private void StopRandomReplays()
    {
        if (randomReplayRoutine != null)
        {
            StopCoroutine(randomReplayRoutine);
            randomReplayRoutine = null;
        }

        StopSegmentInstance();
    }

    private void StopSegmentInstance()
    {
        if (segmentInstance.isValid())
        {
            segmentInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            segmentInstance.release();
        }

        segmentInstance.clearHandle();
    }

    private void TryStartScenario()
    {
        if (scenarioStarted)
        {
            return;
        }

        if (!overrideConditions && !IsWithinActiveHours())
        {
            return;
        }

        StartNextScenario();
    }

    private IEnumerator TypeText(
        TMP_Text text,
        float delay
    )
    {
        text.ForceMeshUpdate();

        text.maxVisibleCharacters = 0;

        int characterCount =
            text.textInfo.characterCount;

        for (int i = 0; i <= characterCount; i++)
        {
            text.maxVisibleCharacters = i;

            yield return new WaitForSeconds(delay);
        }

        text.maxVisibleCharacters = characterCount;
    }

    [System.Serializable]
    public class AudioStartPoint
    {
        [Tooltip("Name of the FMOD timeline marker, e.g. Start_1.")]
        public string markerName = "Start_1";

        [Tooltip("Timeline position of the marker in seconds (as shown in FMOD Studio). " +
                 "FMOD's runtime API cannot look up marker positions by name.")]
        [UnityEngine.Serialization.FormerlySerializedAs("positionMs")]
        public float positionSeconds = 0f;
    }

    [System.Serializable]
    public class Scenario
    {

        [Header("Audio (FMOD Event Emitter object)")]
        public GameObject audioEmitterObject;

        [Tooltip("Loop-region start markers in the event's timeline. The first entry (Start_1) " +
                 "plays during the black screen; afterwards a random entry is replayed periodically. " +
                 "Leave empty to play the whole event during the black screen instead.")]
        public List<AudioStartPoint> audioStartPoints = new List<AudioStartPoint>();

        [Header("Black Screen Panel")]
        public Image blackScreenPanel;

        public Button button;

        [Header("Timing")]
        public float characterDelay = 0.03f;
        public float countdownInterval = 1f;
        public float fadeInDuration = 3f;
        public float fadeOutDuration = 3f;

        [Header("3 → 1 Countdown")]
        public GameObject preCountdownObject;
        public TMP_Text preCountdownText;
        public float randomReplayMaxDelay = 20f;

        public float randomReplayMinDelay = 5f;
        [Header("Sub-Interactive")]
        [Tooltip("Enabled when this sub scenario is picked, disabled when it ends.")]
        [IndexedLabel("Sub-Interactive_Interactive{0}")]
        public GameObject subInteractiveObject;
        public TMP_Text text;
        [Header("Scenario UI")]
        public GameObject textObject;
    }

    [System.Serializable]
    public class Tier1Sound
    {
        public string name = "Tier 1 Sound";

        [Tooltip("The 4 Sub Scenarios that belong to this Tier 1 Sound.")]
        public Scenario[] subScenarios = new Scenario[SubScenarioCount];
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(IndexedLabelAttribute))]
public class IndexedLabelDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        string path = property.propertyPath;
        int open = path.LastIndexOf("data[");

        if (open >= 0)
        {
            int start = open + "data[".Length;
            int end = path.IndexOf(']', start);

            if (end > start && int.TryParse(path.Substring(start, end - start), out int index))
            {
                string format = ((IndexedLabelAttribute)attribute).format;
                label = new GUIContent(string.Format(format, index + 1), label.tooltip);
            }
        }

        UnityEditor.EditorGUI.PropertyField(position, property, label);
    }
}
#endif