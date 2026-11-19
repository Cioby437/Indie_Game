using System.Collections;
using Cinemachine;
using UnityEngine;

// The opening cutscene, played as the "Cutscene" stage.
//
//  1. Eyes closed (black), lying in bed looking at the ceiling. Breathing.
//  2. Eyes open hesitantly, blinking, vision blurry and over-bright until it adjusts.
//  3. Looks around the room.
//  4. Sits up, stands up and walks slowly to the window (Cinemachine dolly track + head bob).
//  5. At the window: a rabbit eating in the field. Vision focuses on it.
//  6. The rabbit stops, looks ahead, opens its mouth and screams (sound: add it to RabbitActor).
//  7. It snaps its head to the player and sprints at the window.
//  8. The player flinches back, every light goes out, and the Gameplay Loop starts in the dark.
//     The lights flicker back on a moment later.
//
// All cameras are Cinemachine virtual cameras. Move them, the path and the look points
// in the Scene view to frame the shots; the timings are below.
public class CutsceneStage : GameStage
{
    [Header("Cinemachine")]
    public CinemachineBrain brain;
    [Tooltip("Rides the walk path (Tracked Dolly) from the bed to the window and looks at the Look Target.")]
    public CinemachineVirtualCamera bodyCam;
    [Tooltip("At the window, looking at the rabbit.")]
    public CinemachineVirtualCamera windowCam;
    [Tooltip("A step back from the window (the flinch).")]
    public CinemachineVirtualCamera stepBackCam;
    public CinemachineSmoothPath walkPath;
    [Tooltip("What the body camera looks at. Moved between the look points by this script.")]
    public Transform lookTarget;
    public int activePriority = 100;

    [Header("Look points")]
    [Tooltip("Where the eyes go while lying in bed. The first one is the ceiling.")]
    public Transform[] wakeLookPoints = new Transform[0];
    [Tooltip("Where the player looks while sitting up and walking.")]
    public Transform getUpLookPoint;
    [Tooltip("Just outside the window: where the player looks while arriving.")]
    public Transform windowLookPoint;

    [Header("Walk path (0 = lying in bed, 1 = at the window)")]
    [Range(0f, 1f)] public float sitUpPathPosition = 0.12f;
    [Range(0f, 1f)] public float standPathPosition = 0.28f;

    [Header("Actors")]
    public CutsceneEyes eyes;
    public LightBlackout lights;
    public RabbitActor rabbit;
    [Tooltip("Where the rabbit sprints to (just outside the window).")]
    public Transform rabbitRunTarget;

    [Header("Timing: waking up (seconds)")]
    public float eyesClosedTime = 3f;
    public float adjustTime = 4f;
    public float lookTravelTime = 1.8f;
    public float lookHoldTime = 1.3f;

    [Header("Timing: getting up and walking")]
    public float sitUpTime = 2.6f;
    public float standUpTime = 2f;
    public float walkTime = 7f;
    public float headBobHeight = 0.03f;
    public float stepsPerSecond = 1.6f;

    [Header("Timing: window and rabbit")]
    public float toWindowCamBlend = 1.6f;
    public float windowFieldOfView = 40f;
    public float watchRabbitTime = 4f;
    public float lookAheadTime = 1.6f;
    public float screamTime = 2.4f;
    public float afterSnapHold = 0.5f;
    [Tooltip("How long the rabbit runs before the player flinches back.")]
    public float runBeforeFlinch = 0.9f;
    public float stepBackBlend = 0.4f;

    [Header("Timing: blackout")]
    public float blackoutHold = 1.6f;
    [Tooltip("Seconds after the Gameplay Loop starts before the lights come back.")]
    public float lightsBackAfter = 2.5f;

    [Header("Breathing / handheld noise (amplitude)")]
    public float lyingNoise = 0.25f;
    public float walkingNoise = 0.6f;
    public float windowNoise = 0.3f;
    public float screamNoise = 1.6f;

    private Coroutine sequence;
    private CinemachineBlendDefinition savedBlend;
    private bool blendSaved;
    private CinemachineTrackedDolly dolly;
    private bool focusOnRabbit;

    // ---------------------------------------------------------------- stage

    protected override void OnBegin()
    {
        if (brain == null || bodyCam == null || eyes == null)
        {
            Debug.LogWarning("[Cutscene] Missing references (brain, body cam or eyes). Skipping the cutscene.");
            Complete();
            return;
        }

        sequence = StartCoroutine(Play());
    }

    protected override void OnApplyEndState()
    {
        if (sequence != null)
        {
            StopCoroutine(sequence);
            sequence = null;
        }

        StopAllCoroutines();
        focusOnRabbit = false;

        HandOffToGameplay();

        if (eyes != null) eyes.Release();
        if (rabbit != null) rabbit.Hide();
        if (lights != null) lights.RestoreNow();
    }

    private void Update()
    {
        if (focusOnRabbit && eyes != null && rabbit != null && brain != null)
        {
            eyes.focusDistance = Vector3.Distance(brain.transform.position, rabbit.transform.position);
        }
    }

    // ---------------------------------------------------------------- the sequence

    private IEnumerator Play()
    {
        // ---- setup
        savedBlend = brain.m_DefaultBlend;
        blendSaved = true;
        dolly = bodyCam.GetCinemachineComponent<CinemachineTrackedDolly>();

        if (lights != null) lights.RestoreNow();

        if (rabbit != null)
        {
            rabbit.ResetToStart();
            rabbit.StartEating();
        }

        SetDolly(0f, 0f);
        PlaceLookTarget(wakeLookPoints.Length > 0 ? wakeLookPoints[0] : getUpLookPoint);
        SetNoise(bodyCam, lyingNoise);
        Live(bodyCam, CinemachineBlendDefinition.Style.Cut, 0f);

        eyes.Activate(0f, 1f, 0f);

        // ---- 1. eyes closed
        yield return new WaitForSeconds(eyesClosedTime);

        // ---- 2. eyes open, hesitant, blinking until vision adjusts
        yield return eyes.To(0.12f, 0.6f, 1f, 0.2f);
        yield return new WaitForSeconds(0.35f);
        yield return eyes.To(0f, 0.3f);
        yield return new WaitForSeconds(0.8f);
        yield return eyes.To(0.35f, 0.7f, 0.95f, 0.8f);     // light floods in
        yield return new WaitForSeconds(0.4f);
        yield return eyes.To(0.06f, 0.25f);
        yield return new WaitForSeconds(0.3f);
        yield return eyes.To(0.7f, 0.8f, 0.8f, 0.6f);
        yield return new WaitForSeconds(0.6f);
        yield return eyes.Blink(0.1f, 0.12f, 0.3f);
        yield return eyes.To(1f, 1f, 0.6f, 0.3f);

        // Vision slowly adjusts while looking around, with a few blinks.
        StartCoroutine(eyes.AdjustVision(adjustTime));

        // ---- 3. look around the room
        for (int i = 1; i < wakeLookPoints.Length; i++)
        {
            yield return MoveLookTarget(wakeLookPoints[i], lookTravelTime);

            if (i % 2 == 1)
            {
                yield return eyes.Blink();
            }

            yield return new WaitForSeconds(lookHoldTime);
        }

        // ---- 4. sit up, stand up, walk to the window
        SetNoise(bodyCam, walkingNoise);
        StartCoroutine(MoveLookTarget(getUpLookPoint, sitUpTime));
        yield return DollyTo(sitUpPathPosition, sitUpTime, false);
        yield return new WaitForSeconds(0.6f);
        yield return eyes.Blink();
        yield return DollyTo(standPathPosition, standUpTime, false);
        yield return new WaitForSeconds(0.4f);

        StartCoroutine(MoveLookTarget(windowLookPoint, walkTime * 0.8f));
        yield return DollyTo(1f, walkTime, true);

        // ---- 5. at the window: the rabbit
        SetNoise(windowCam, windowNoise);
        if (rabbit != null) windowCam.LookAt = rabbit.transform;
        Live(windowCam, CinemachineBlendDefinition.Style.EaseInOut, toWindowCamBlend);
        StartCoroutine(Fov(windowCam, windowFieldOfView, toWindowCamBlend + watchRabbitTime));
        focusOnRabbit = true;
        yield return new WaitForSeconds(toWindowCamBlend + watchRabbitTime);

        if (rabbit != null)
        {
            // ---- 6. it stops eating, looks ahead, screams
            rabbit.LookAhead();
            yield return new WaitForSeconds(lookAheadTime);

            StartCoroutine(NoiseBurst(windowCam, screamNoise, screamTime));
            yield return rabbit.Scream(screamTime);

            // ---- 7. snaps to the player and sprints
            yield return rabbit.SnapLookAt(brain.transform.position);
            yield return new WaitForSeconds(afterSnapHold);

            Vector3 runTarget = rabbitRunTarget != null ? rabbitRunTarget.position : brain.transform.position;
            StartCoroutine(rabbit.RunTo(runTarget));
            yield return new WaitForSeconds(runBeforeFlinch);
        }

        // ---- 8. flinch back, blackout, gameplay
        focusOnRabbit = false;

        if (stepBackCam != null)
        {
            SetNoise(stepBackCam, screamNoise);
            Live(stepBackCam, CinemachineBlendDefinition.Style.EaseOut, stepBackBlend);
        }

        StartCoroutine(eyes.Blink(0.06f, 0.05f, 0.12f));
        yield return new WaitForSeconds(stepBackBlend);

        if (lights != null) lights.GoDark();
        if (rabbit != null) rabbit.Hide();
        yield return new WaitForSeconds(blackoutHold);

        HandOffToGameplay();
        StartCoroutine(eyes.FadeOut(0.3f));

        if (lights != null) lights.RestoreAfter(lightsBackAfter);

        sequence = null;
        Complete();
    }

    // ---------------------------------------------------------------- camera helpers

    private void Live(CinemachineVirtualCamera cam, CinemachineBlendDefinition.Style style, float time)
    {
        brain.m_DefaultBlend = new CinemachineBlendDefinition(style, time);

        SetPriority(bodyCam, cam == bodyCam);
        SetPriority(windowCam, cam == windowCam);
        SetPriority(stepBackCam, cam == stepBackCam);
    }

    private void SetPriority(CinemachineVirtualCamera cam, bool live)
    {
        if (cam != null) cam.Priority = live ? activePriority : 0;
    }

    // Cuts back to the gameplay cameras (it's dark, so a cut is invisible).
    private void HandOffToGameplay()
    {
        if (brain != null)
        {
            brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
        }

        SetPriority(bodyCam, false);
        SetPriority(windowCam, false);
        SetPriority(stepBackCam, false);

        if (brain != null && blendSaved)
        {
            StartCoroutine(RestoreBlendNextFrame());
        }
    }

    private IEnumerator RestoreBlendNextFrame()
    {
        yield return null;
        yield return null;
        brain.m_DefaultBlend = savedBlend;
        blendSaved = false;
    }

    private void SetDolly(float position, float bob)
    {
        if (dolly == null) return;
        dolly.m_PositionUnits = CinemachinePathBase.PositionUnits.Normalized;
        dolly.m_PathPosition = position;
        dolly.m_PathOffset = new Vector3(0f, bob, 0f);
    }

    private IEnumerator DollyTo(float target, float duration, bool walking)
    {
        if (dolly == null) yield break;

        float from = dolly.m_PathPosition;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            // Walking: steady pace with soft start and stop. Sitting/standing: slow ease.
            float eased = walking ? Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, k) * 0.5f + k * 0.5f)
                                  : Mathf.SmoothStep(0f, 1f, k);
            float bob = walking
                ? Mathf.Abs(Mathf.Sin(t * stepsPerSecond * Mathf.PI)) * headBobHeight * Mathf.Sin(Mathf.PI * k)
                : 0f;

            SetDolly(Mathf.Lerp(from, target, eased), bob);
            yield return null;
        }

        SetDolly(target, 0f);
    }

    private void PlaceLookTarget(Transform point)
    {
        if (lookTarget != null && point != null)
        {
            lookTarget.position = point.position;
        }
    }

    private IEnumerator MoveLookTarget(Transform point, float duration)
    {
        if (lookTarget == null || point == null) yield break;

        Vector3 from = lookTarget.position;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            lookTarget.position = Vector3.Lerp(from, point.position, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        lookTarget.position = point.position;
    }

    private static void SetNoise(CinemachineVirtualCamera cam, float amplitude)
    {
        if (cam == null) return;
        CinemachineBasicMultiChannelPerlin noise = cam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        if (noise != null) noise.m_AmplitudeGain = amplitude;
    }

    private static IEnumerator NoiseBurst(CinemachineVirtualCamera cam, float amplitude, float duration)
    {
        if (cam == null) yield break;
        CinemachineBasicMultiChannelPerlin noise = cam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        if (noise == null) yield break;

        float normal = noise.m_AmplitudeGain;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            noise.m_AmplitudeGain = Mathf.Lerp(normal, amplitude, Mathf.Sin(Mathf.PI * t / duration));
            yield return null;
        }

        noise.m_AmplitudeGain = normal;
    }

    private static IEnumerator Fov(CinemachineVirtualCamera cam, float target, float duration)
    {
        if (cam == null) yield break;

        float from = cam.m_Lens.FieldOfView;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            cam.m_Lens.FieldOfView = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        cam.m_Lens.FieldOfView = target;
    }
}
