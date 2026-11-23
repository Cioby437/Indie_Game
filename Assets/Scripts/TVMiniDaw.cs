using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

// The cassette goes into the TV and the TV becomes a tiny DAW for its recording.
//
// Flow: the player picks up the Casette Player, puts a cassette in, clicks the held player and hears the
// recording (S_Interactive_VoiceLine_1) to the end. Then clicking the TV (TV camera active) calls Insert().
// Escape or the back button calls Eject().
//
// The DAW screen (drawn into one texture on the quad in front of the TV glass):
//   XY pad          x = the event's NotchFreq parameter (0 - 1; the Hz on screen is only for show)
//                   y = the pitch on screen (-12 - +12), sent as is to the event's Pitch parameter (-12 - 12 in FMOD)
//   PITCH / FREQUENCY readouts just above the transport
//   transport       play / pause button and a position slider that follows the recording (click / drag to seek)
// Arrow keys step the FREQUENCY / PITCH readouts by one step (hold to repeat, Shift = x10), Space = play / pause.
//
// The recording loops at the TV. The pad only sets the event's "NotchFreq" and "Pitch" parameters;
// what they do to the sound is up to the automation in FMOD Studio.
// When the settings hit the target in the SecretLayerPuzzle, the hidden event (Hidden_1) fades in.
public class TVMiniDaw : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The held Casette Player. Its cassette is the one that goes into the TV.")]
    public CasettePlayerInspect casettePlayerInspect;

    [Tooltip("Camera that renders the game (the one with the CinemachineBrain). Defaults to Camera.main.")]
    public Camera raycastCamera;

    [Header("Recording")]
    [Tooltip("Keep looping the recording while the cassette is in the TV.")]
    public bool loop = true;

    [Header("Puzzle")]
    [Tooltip("Used when the inserted cassette has no puzzle of its own.")]
    public SecretLayerPuzzle puzzle;

    [Tooltip("How fast the hidden layer fades in / out (volume per second).")]
    public float revealFadeSpeed = 3f;

    [Header("DAW Screen")]
    [Tooltip("A Quad placed just in front of the TV glass (needs a MeshRenderer and a MeshCollider). " +
             "It stays black until the cassette is inserted; its collider is only on while the DAW is open.")]
    public GameObject dawScreen;

    [Tooltip("Texture slot on the DAW screen's material. HDRP Unlit: _UnlitColorMap.")]
    public string screenTextureProperty = "_UnlitColorMap";

    public int textureWidth = 512;
    public int textureHeight = 256;

    [Tooltip("Flip these if dragging moves the controls the wrong way (depends on the quad's rotation).")]
    public bool invertHorizontal = false;
    public bool invertVertical = false;

    [Header("Frequency (left / right) = FMOD parameter NotchFreq, 0 - 1")]
    [Tooltip("FMOD parameter on the recording's event. 0 = left edge of the screen, 1 = right edge.")]
    public string notchParameter = "NotchFreq";

    [Tooltip("NotchFreq value when the cassette goes into the TV.")]
    [Range(0f, 1f)] public float startNotchValue = 0.41f;

    [Tooltip("Hz shown on screen at NotchFreq = 0 and = 1. Only for show: NotchFreq itself is what FMOD gets.")]
    public float notchDisplayMinHz = 44f;
    public float notchDisplayMaxHz = 14000f;

    [Tooltip("Off: the Hz shown moves evenly between the two values. On: log spacing.")]
    public bool notchDisplayLogarithmic = false;

    [Tooltip("The Hz shown is rounded to this step (so the player can land on e.g. exactly 1000). 0 = no rounding.")]
    public float notchDisplayStep = 10f;

    [Header("Pitch (down / up) = FMOD parameter Pitch, 0.5 - 2")]
    [Tooltip("FMOD parameter on the recording's event.")]
    public string pitchParameter = "Pitch";

    [Tooltip("Pitch shown on screen at the bottom and top of the pad.")]
    public float pitchDisplayMin = -12f;
    public float pitchDisplayMax = 12f;

    [Tooltip("The pitch snaps to this step (1 = whole numbers, so the player can land on exactly 5). 0 = no snapping.")]
    public float pitchStep = 1f;

    [Tooltip("Pitch on screen when the cassette goes into the TV (-12 - +12).")]
    public float startPitch = 0f;

    [Header("Screen Layout (pixels)")]
    [Tooltip("Height of the transport strip at the bottom (play / pause and the position slider).")]
    public int transportHeight = 30;

    [Tooltip("Height of the PITCH / FREQUENCY text row above the transport.")]
    public int readoutHeight = 22;

    [Tooltip("Size of the readout text (1 = 5x7 pixel letters).")]
    [Range(1, 4)] public int readoutTextScale = 2;

    [Header("Screen Colours")]
    public Color32 backgroundColor = new Color32(4, 14, 10, 255);
    public Color32 screenOffColor = new Color32(0, 0, 0, 255);
    public Color32 spectrumColor = new Color32(90, 255, 160, 255);
    public Color32 waveformColor = new Color32(30, 90, 60, 255);
    public Color32 notchColor = new Color32(255, 200, 60, 255);
    public Color32 pitchColor = new Color32(80, 160, 255, 255);
    public Color32 panelColor = new Color32(2, 8, 6, 255);
    public Color32 textColor = new Color32(150, 255, 190, 255);
    public Color32 sliderTrackColor = new Color32(30, 70, 50, 255);
    public Color32 sliderFillColor = new Color32(90, 255, 160, 255);

    [Header("Static (gets worse near the hidden sound)")]
    [Tooltip("The spectrum slowly turns this colour as the player gets close.")]
    public Color32 closeWaveformColor = new Color32(230, 25, 20, 255);
    [Tooltip("How fast the colour follows the player's closeness (0-1 per second). Low = slow drift to red.")]
    public float colourChangeSpeed = 0.35f;
    [Tooltip("How fast the static/shake follows the player's closeness (0-1 per second).")]
    public float staticResponseSpeed = 2.5f;
    [Tooltip("1 = static grows evenly; higher = barely any until close, then violent.")]
    public float staticCurve = 2f;
    [Tooltip("Max whole-picture shake in pixels at full closeness.")]
    public float maxShakePixels = 18f;
    [Tooltip("Max random vertical jitter per spectrum column, in pixels.")]
    public float maxWaveJitterPixels = 40f;
    [Tooltip("Max horizontal tearing of screen rows (VHS tracking), in pixels.")]
    public int maxRowTearPixels = 60;
    [Tooltip("Fraction of pixels turned to snow at full closeness.")]
    [Range(0f, 1f)] public float maxSnowAmount = 0.55f;
    [Tooltip("Optional FMOD parameter (0-1) on the recording's event that receives the static amount.")]
    public string staticParameter = "";

    [Header("When The Haunted Frequency Is Found")]
    [Tooltip("One-shot played at the TV when the puzzle is solved. Empty = the path below.")]
    public EventReference solvedOneShot;
    public string solvedOneShotPath = "event:/One-Shots/OneShot_5";

    [Tooltip("The only thing on the TV screen after the puzzle is solved.")]
    public string solvedMessage = "LOOK AWAY";
    public Color32 solvedMessageColor = new Color32(230, 25, 20, 255);
    [Range(1, 12)] public int solvedMessageScale = 8;

    [Tooltip("How long the message stays before the TV turns off. 0 = as long as the one-shot (at least 2 s).")]
    public float solvedMessageSeconds = 0f;

    [Tooltip("Optional. Switched off together with the TV (e.g. the TV's screen light, glow or hum emitter).")]
    public GameObject[] turnOffWithTV;

    // True once the TV has switched itself off after the puzzle; the cassette can't go in again.
    public bool IsTurnedOff { get; private set; }

    private bool inFinale;

    [Header("Events")]
    public UnityEvent onInserted;
    public UnityEvent onEjected;
    public UnityEvent onSolved;

    // =========================================================
    // STATE
    // =========================================================

    private CassetteItem insertedCassette;
    private SecretLayerPuzzle activePuzzle;
    private EventReference recordingEvent;

    private FMOD.Studio.EventInstance playback;
    private FMOD.Studio.EventInstance hidden;
    private int lengthMs;
    private int hiddenLengthMs;
    private bool paused;

    // FFT on the recording's channel group (for the spectrum on screen).
    private DSP fft;
    private ChannelGroup dspGroup;
    private float[] spectrum = new float[0];
    private int outputRate = 48000;

    private float notch01 = 0.41f;
    private float pitch01 = 0.5f;
    private float reveal = 0f;
    private float holdTimer = 0f;
    private float staticAmount = 0f;    // 0-1, drives snow/shake/tearing
    private float colourAmount = 0f;    // 0-1, drives green -> red (slower)
    private uint noiseState = 2463534242u;
    private bool warnedSameHiddenEvent;

    private enum Drag { None, Pad, Slider }

    private Drag drag = Drag.None;
    private bool resumeAfterSeek;

    private int ejectedFrame = -1;
    private int insertedFrame = -1;

    private Texture2D screenTexture;
    private bool screenTextureAssigned;
    private Color32[] pixels;
    private Color32[] rowBuffer;
    private MeshCollider screenCollider;

    // =========================================================
    // PUBLIC
    // =========================================================

    public bool IsOpen { get; private set; }
    public bool Solved { get; private set; }

    // CursorCameraManager ignores the mouse while this is true (also on the frame of ejecting).
    public bool BlocksInput
    {
        get { return IsOpen || ejectedFrame == Time.frameCount; }
    }

    public bool CanInsert
    {
        get { return InsertBlocker == null; }
    }

    // Why the cassette can't go in right now (null = it can). Logged when the TV is clicked.
    public string InsertBlocker
    {
        get
        {
            if (IsTurnedOff) return "the TV has turned itself off.";
            if (IsOpen) return "the cassette is already in the TV.";
            if (casettePlayerInspect == null) return "no Casette Player Inspect is assigned on TVMiniDaw.";
            if (!casettePlayerInspect.IsHeld) return "the Casette Player hasn't been picked up yet.";
            if (!casettePlayerInspect.HasCassette) return "there is no cassette in the Casette Player.";
            if (!casettePlayerInspect.CurrentCassette.HasBeenHeard) return "the cassette hasn't been listened to yet (click the held player and let the recording play to the end).";
            return null;
        }
    }

    // Frequency shown on screen (and used by the puzzle), from the NotchFreq value. Only for show.
    public float NotchHz
    {
        get { return Snap(DisplayHz(notch01), notchDisplayStep); }
    }

    // NotchFreq parameter value (0 - 1).
    public float NotchValue
    {
        get { return notch01; }
    }

    // Pitch shown on screen (and used by the puzzle): -12 at the bottom, +12 at the top, snapped to Pitch Step.
    public float PitchSemitones
    {
        get
        {
            float pitch = Snap(Mathf.Lerp(pitchDisplayMin, pitchDisplayMax, pitch01), pitchStep);
            return Mathf.Clamp(pitch, Mathf.Min(pitchDisplayMin, pitchDisplayMax), Mathf.Max(pitchDisplayMin, pitchDisplayMax));
        }
    }

    // Pitch parameter sent to FMOD: exactly the pitch on screen. (The event's Pitch parameter runs -12 - 12;
    // its automation in FMOD Studio turns that into the pitch shifter's 0.5 - 2.)
    public float PitchValue
    {
        get { return PitchSemitones; }
    }

    private static float Snap(float value, float step)
    {
        return step > 0f ? Mathf.Round(value / step) * step : value;
    }

    private float DisplayHz(float value01)
    {
        if (notchDisplayLogarithmic && notchDisplayMinHz > 0f)
        {
            return notchDisplayMinHz * Mathf.Pow(notchDisplayMaxHz / notchDisplayMinHz, value01);
        }

        return Mathf.Lerp(notchDisplayMinHz, notchDisplayMaxHz, value01);
    }

    public bool IsPaused
    {
        get { return paused; }
    }

    // Full state report for debugging the insert step.
    public string DebugStatus()
    {
        string holderInfo = casettePlayerInspect == null
            ? "  Casette Player Inspect: NOT ASSIGNED on TVMiniDaw\n"
            : "  Casette Player held: " + casettePlayerInspect.IsHeld +
              ", cassette in it: " + (casettePlayerInspect.HasCassette ? casettePlayerInspect.CurrentCassette.name : "none") +
              ", heard: " + (casettePlayerInspect.HasCassette && casettePlayerInspect.CurrentCassette.HasBeenHeard) + "\n";

        return "[TV Mini DAW] Status:\n" +
               holderInfo +
               "  DAW screen: " + (dawScreen == null ? "NOT ASSIGNED" : dawScreen.name) + "\n" +
               "  Already open: " + IsOpen + "\n" +
               "  Can insert: " + CanInsert + (InsertBlocker != null ? " (" + InsertBlocker + ")" : "");
    }

    // Switch to another puzzle (e.g. from another script).
    public void SetPuzzle(SecretLayerPuzzle newPuzzle)
    {
        puzzle = newPuzzle;
        activePuzzle = newPuzzle;
        Solved = false;
        holdTimer = 0f;
    }

    public void TogglePause()
    {
        if (!IsOpen)
        {
            return;
        }

        SetPaused(!paused);
    }

    // =========================================================
    // INSERT / EJECT
    // =========================================================

    public void Insert()
    {
        if (!CanInsert)
        {
            return;
        }

        insertedCassette = casettePlayerInspect.CurrentCassette;
        recordingEvent = insertedCassette.GetRecording();

        if (recordingEvent.IsNull)
        {
            Debug.LogWarning("TVMiniDaw: the cassette '" + insertedCassette.name + "' has no recording.");
            insertedCassette = null;
            return;
        }

        // A different cassette (or puzzle) than last time starts unsolved.
        SecretLayerPuzzle newPuzzle = insertedCassette.puzzle != null ? insertedCassette.puzzle : puzzle;
        if (newPuzzle != activePuzzle)
        {
            activePuzzle = newPuzzle;
            Solved = false;
        }

        IsOpen = true;
        insertedFrame = Time.frameCount;
        reveal = 0f;
        staticAmount = 0f;
        colourAmount = 0f;
        holdTimer = 0f;
        paused = false;
        drag = Drag.None;

        // Starting point on the pad.
        notch01 = Mathf.Clamp01(startNotchValue);
        pitch01 = Mathf.Clamp01(Mathf.InverseLerp(pitchDisplayMin, pitchDisplayMax, startPitch));

        // The cassette is "inside" the TV now; the held player stops and hides.
        casettePlayerInspect.StopCassette();
        casettePlayerInspect.SetHeldVisible(false);

        RuntimeManager.CoreSystem.getSoftwareFormat(out outputRate, out _, out _);

        playback = RuntimeManager.CreateInstance(recordingEvent);
        playback.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
        playback.getDescription(out FMOD.Studio.EventDescription description);
        description.getLength(out lengthMs);

        if (activePuzzle != null && UsesSeparateHiddenEvent())
        {
            hidden = RuntimeManager.CreateInstance(activePuzzle.hiddenEvent);
            hidden.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
            hidden.getDescription(out FMOD.Studio.EventDescription hiddenDescription);
            hiddenDescription.getLength(out hiddenLengthMs);
        }

        SetReveal(Solved && activePuzzle != null && activePuzzle.stayRevealedWhenSolved ? 1f : 0f);
        ApplyParameters();
        StartFromTop();

        SetupScreen();

        Debug.Log("[TV Mini DAW] Cassette '" + insertedCassette.name + "' inserted (" +
                  (lengthMs / 1000f).ToString("0.0") + " s, looping: " + loop + ").");

        onInserted.Invoke();
    }

    public void Eject()
    {
        // The ending (message, then TV off) plays out on its own.
        if (!IsOpen || inFinale)
        {
            return;
        }

        IsOpen = false;
        ejectedFrame = Time.frameCount;
        drag = Drag.None;

        DetachDsps();
        ReleaseInstance(ref playback, FMOD.Studio.STOP_MODE.IMMEDIATE);
        ReleaseInstance(ref hidden, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);

        // Back to a black screen. The quad stays on; only its collider turns off,
        // so clicks reach the TV behind it again.
        ShowBlackScreen();

        if (casettePlayerInspect != null)
        {
            casettePlayerInspect.SetHeldVisible(true);
        }

        insertedCassette = null;

        Debug.Log("[TV Mini DAW] Cassette ejected.");

        onEjected.Invoke();
    }

    // =========================================================
    // PLAYBACK
    // =========================================================

    private static void ReleaseInstance(ref FMOD.Studio.EventInstance instance, FMOD.Studio.STOP_MODE mode)
    {
        if (!instance.isValid())
        {
            return;
        }

        instance.stop(mode);
        instance.release();
        instance.clearHandle();
    }

    // (Re)starts the recording and the hidden layer together, so they stay in sync on every loop.
    private void StartFromTop()
    {
        if (playback.isValid())
        {
            playback.start();
        }

        if (hidden.isValid())
        {
            hidden.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            hidden.start();
        }

        SetPausedInternal(paused);
    }

    private void SetPaused(bool value)
    {
        paused = value;

        // Pressing play after the recording ended (loop off) starts it again.
        if (!paused && playback.isValid())
        {
            playback.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state);

            if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
            {
                StartFromTop();
                return;
            }
        }

        SetPausedInternal(paused);
    }

    private void SetPausedInternal(bool value)
    {
        if (playback.isValid()) playback.setPaused(value);
        if (hidden.isValid()) hidden.setPaused(value);
    }

    private int PositionMs()
    {
        if (!playback.isValid())
        {
            return 0;
        }

        playback.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state);
        if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            return loop ? 0 : lengthMs;
        }

        playback.getTimelinePosition(out int position);
        return Mathf.Clamp(position, 0, Mathf.Max(lengthMs, 0));
    }

    private void Seek(float position01)
    {
        if (!playback.isValid() || lengthMs <= 0)
        {
            return;
        }

        int ms = Mathf.RoundToInt(Mathf.Clamp01(position01) * (lengthMs - 1));

        playback.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state);
        if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            StartFromTop();
        }

        playback.setTimelinePosition(ms);

        if (hidden.isValid())
        {
            hidden.setTimelinePosition(hiddenLengthMs > 0 ? Mathf.Min(ms, hiddenLengthMs - 1) : ms);
        }
    }

    // Loops the recording (or stops at the end when Loop is off).
    private void UpdateLoop()
    {
        if (!playback.isValid() || paused)
        {
            return;
        }

        playback.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state);

        if (state != FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            return;
        }

        if (loop)
        {
            StartFromTop();
        }
        else
        {
            paused = true;
            if (hidden.isValid()) hidden.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        }
    }

    private void ApplyParameters()
    {
        if (!string.IsNullOrEmpty(staticParameter) && playback.isValid())
        {
            playback.setParameterByName(staticParameter, staticAmount);
        }

        if (playback.isValid())
        {
            if (!string.IsNullOrEmpty(notchParameter))
            {
                playback.setParameterByName(notchParameter, notch01);
            }

            if (!string.IsNullOrEmpty(pitchParameter))
            {
                playback.setParameterByName(pitchParameter, PitchValue);
            }
        }
    }

    // =========================================================
    // FFT (spectrum of the recording, for the screen)
    // =========================================================

    private void UpdateDsps()
    {
        // The instance's channel group only exists once FMOD has started it,
        // and can change when the loop restarts, so it is checked every frame.
        if (playback.getChannelGroup(out ChannelGroup group) != RESULT.OK || !group.hasHandle())
        {
            return;
        }

        if (group.handle != dspGroup.handle)
        {
            DetachDsps();

            if (RuntimeManager.CoreSystem.createDSPByType(DSP_TYPE.FFT, out fft) != RESULT.OK)
            {
                return;
            }

            fft.setParameterInt((int)DSP_FFT.WINDOWSIZE, 2048);

            // HEAD = the end of the event's signal chain, so the notch shows up as a dip.
            if (group.addDSP(CHANNELCONTROL_DSP_INDEX.HEAD, fft) != RESULT.OK)
            {
                fft.release();
                fft.clearHandle();
                return;
            }

            dspGroup = group;
        }

        if (fft.getParameterData((int)DSP_FFT.SPECTRUMDATA, out IntPtr data, out _) != RESULT.OK)
        {
            return;
        }

        DSP_PARAMETER_FFT fftData = Marshal.PtrToStructure<DSP_PARAMETER_FFT>(data);

        if (fftData.numchannels <= 0 || fftData.length <= 0)
        {
            return;
        }

        if (spectrum.Length != fftData.length)
        {
            spectrum = new float[fftData.length];
        }

        fftData.getSpectrum(0, ref spectrum);
    }

    private void DetachDsps()
    {
        // The old channel group may already be gone; errors here are harmless.
        ReleaseDsp(ref fft);

        dspGroup.clearHandle();
        spectrum = new float[0];
    }

    private void ReleaseDsp(ref DSP dsp)
    {
        if (!dsp.hasHandle())
        {
            return;
        }

        if (dspGroup.hasHandle())
        {
            dspGroup.removeDSP(dsp);
        }

        dsp.release();
        dsp.clearHandle();
    }

    // =========================================================
    // INPUT
    // =========================================================

    private int PadBottom
    {
        get { return transportHeight + readoutHeight; }
    }

    // Play / pause button area (control-space pixels).
    private RectInt PlayButtonRect
    {
        get
        {
            int size = transportHeight - 8;
            return new RectInt(8, 4, size, size);
        }
    }

    // Slider track area (control-space pixels).
    private RectInt SliderRect
    {
        get
        {
            RectInt button = PlayButtonRect;
            int left = button.xMax + 10;
            int right = textureWidth - 8 - TextWidth("00:00/00:00", 1) - 8;
            return new RectInt(left, transportHeight / 2 - 3, Mathf.Max(10, right - left), 6);
        }
    }

    private void HandleInput()
    {
        Mouse mouse = Mouse.current;

        bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame && Time.frameCount != insertedFrame;
        bool held = mouse != null && mouse.leftButton.isPressed && Time.frameCount != insertedFrame;

        if (!held)
        {
            EndSliderDrag();
        }

        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (held && screenCollider != null && TryGetScreenPixel(mouse, out Vector2 p))
        {
            // What the press grabbed decides what the drag controls, until the button is released.
            if (pressed && !overUi)
            {
                if (p.y >= PadBottom)
                {
                    drag = Drag.Pad;
                }
                else if (Expand(PlayButtonRect, 4).Contains(Vector2Int.FloorToInt(p)))
                {
                    TogglePause();
                }
                else if (Expand(SliderRect, 8).Contains(Vector2Int.FloorToInt(p)))
                {
                    drag = Drag.Slider;
                    resumeAfterSeek = !paused;
                    SetPausedInternal(true); // quiet while scrubbing
                }
            }

            if (drag == Drag.Pad)
            {
                notch01 = Mathf.Clamp01(p.x / (textureWidth - 1));

                // Once the player moves it, NotchFreq sits exactly on the value whose Hz is on screen.
                if (notchDisplayStep > 0f)
                {
                    notch01 = Mathf.Clamp01(HzToValue(NotchHz));
                }

                pitch01 = Mathf.Clamp01((p.y - PadBottom) / (textureHeight - 1 - PadBottom));
            }
            else if (drag == Drag.Slider)
            {
                RectInt slider = SliderRect;
                Seek((p.x - slider.xMin) / slider.width);
            }
        }

        if (Keyboard.current != null)
        {
            Keyboard keyboard = Keyboard.current;
            int big = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 10 : 1;

            // One readout step per press (hold to repeat), so exact values like 1000 Hz are easy to reach.
            int freqSteps = KeyRepeat(keyboard.rightArrowKey, ref rightHeld) - KeyRepeat(keyboard.leftArrowKey, ref leftHeld);
            int pitchSteps = KeyRepeat(keyboard.upArrowKey, ref upHeld) - KeyRepeat(keyboard.downArrowKey, ref downHeld);

            if (freqSteps != 0)
            {
                float step = notchDisplayStep > 0f ? notchDisplayStep : 10f;
                notch01 = HzToValue(NotchHz + freqSteps * big * step);
            }

            if (pitchSteps != 0)
            {
                float step = pitchStep > 0f ? pitchStep : 0.1f;
                pitch01 = Mathf.InverseLerp(pitchDisplayMin, pitchDisplayMax, PitchSemitones + pitchSteps * big * step);
            }

            if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
        }

        notch01 = Mathf.Clamp01(notch01);
        pitch01 = Mathf.Clamp01(pitch01);
    }

    [Header("Arrow Keys")]
    [Tooltip("Hold an arrow key this long before it starts repeating.")]
    public float keyRepeatDelay = 0.35f;

    [Tooltip("Seconds between repeats while an arrow key is held.")]
    public float keyRepeatInterval = 0.04f;

    private float rightHeld = -1f, leftHeld = -1f, upHeld = -1f, downHeld = -1f;

    // 1 on the press, then 1 every repeat interval after the delay while held, else 0.
    private int KeyRepeat(UnityEngine.InputSystem.Controls.KeyControl key, ref float heldTime)
    {
        if (!key.isPressed)
        {
            heldTime = -1f;
            return 0;
        }

        if (heldTime < 0f)
        {
            heldTime = 0f;
            return 1;
        }

        float before = heldTime;
        heldTime += Time.unscaledDeltaTime;

        if (heldTime < keyRepeatDelay)
        {
            return 0;
        }

        int repeatsBefore = before < keyRepeatDelay ? 0 : Mathf.FloorToInt((before - keyRepeatDelay) / keyRepeatInterval) + 1;
        int repeatsNow = Mathf.FloorToInt((heldTime - keyRepeatDelay) / keyRepeatInterval) + 1;
        return repeatsNow - repeatsBefore;
    }

    // NotchFreq value (0 - 1) for a Hz shown on screen.
    private float HzToValue(float hz)
    {
        hz = Mathf.Clamp(hz, Mathf.Min(notchDisplayMinHz, notchDisplayMaxHz), Mathf.Max(notchDisplayMinHz, notchDisplayMaxHz));

        if (notchDisplayLogarithmic && notchDisplayMinHz > 0f && hz > 0f)
        {
            return Mathf.Log(hz / notchDisplayMinHz) / Mathf.Log(notchDisplayMaxHz / notchDisplayMinHz);
        }

        return Mathf.InverseLerp(notchDisplayMinHz, notchDisplayMaxHz, hz);
    }

    private void EndSliderDrag()
    {
        if (drag == Drag.Slider)
        {
            paused = !resumeAfterSeek;
            SetPausedInternal(paused);
        }

        drag = Drag.None;
    }

    // Mouse position on the DAW screen, in control-space pixels (left = low Hz, bottom = transport).
    private bool TryGetScreenPixel(Mouse mouse, out Vector2 pixel)
    {
        pixel = Vector2.zero;

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null)
        {
            return false;
        }

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());

        // Only the DAW screen is tested, so nothing else in the room can catch the click.
        if (!screenCollider.Raycast(ray, out RaycastHit hit, 100f))
        {
            return false;
        }

        Vector2 uv = hit.textureCoord;
        float x = invertHorizontal ? 1f - uv.x : uv.x;
        float y = invertVertical ? 1f - uv.y : uv.y;

        pixel = new Vector2(x * (textureWidth - 1), y * (textureHeight - 1));
        return true;
    }

    private static RectInt Expand(RectInt r, int amount)
    {
        return new RectInt(r.x - amount, r.y - amount, r.width + amount * 2, r.height + amount * 2);
    }

    // =========================================================
    // PUZZLE
    // =========================================================

    // The hidden layer is a separate event (Hidden_1) unless the puzzle points at the recording itself.
    private bool UsesSeparateHiddenEvent()
    {
        if (activePuzzle == null || activePuzzle.hiddenEvent.IsNull)
        {
            return false;
        }

        if (!recordingEvent.IsNull && activePuzzle.hiddenEvent.Guid.Equals(recordingEvent.Guid))
        {
            if (!warnedSameHiddenEvent)
            {
                Debug.LogWarning("[TV Mini DAW] The puzzle's Hidden Event is the cassette's recording itself, so it is ignored.");
                warnedSameHiddenEvent = true;
            }

            return false;
        }

        return true;
    }

    private void SetReveal(float value)
    {
        if (activePuzzle == null)
        {
            return;
        }

        // Hidden layer as a track inside the recording's event, automated by the reveal parameter.
        if (!hidden.isValid())
        {
            if (playback.isValid() && !string.IsNullOrEmpty(activePuzzle.revealParameter))
            {
                playback.setParameterByName(activePuzzle.revealParameter, value);
            }

            return;
        }

        // Hidden layer as a separate event (Hidden_1).
        if (string.IsNullOrEmpty(activePuzzle.revealParameter))
        {
            hidden.setVolume(value);
        }
        else
        {
            hidden.setParameterByName(activePuzzle.revealParameter, value);
        }
    }

    private void UpdatePuzzle()
    {
        if (activePuzzle == null)
        {
            return;
        }

        float score = activePuzzle.Evaluate(Mathf.Max(1f, NotchHz), PitchSemitones);

        if (Solved && activePuzzle.stayRevealedWhenSolved)
        {
            score = 1f;
        }

        if (!Solved)
        {
            if (score >= 1f)
            {
                holdTimer += Time.deltaTime;

                if (holdTimer >= activePuzzle.holdToSolveSeconds)
                {
                    Solved = true;
                    StartCoroutine(SolvedSequence());
                    Debug.Log("[TV Mini DAW] Puzzle solved.");
                    onSolved.Invoke();
                }
            }
            else
            {
                holdTimer = 0f;
            }
        }

        // Follows the closeness to the target (louder as the player gets closer, 1 on target),
        // smoothed over Reveal Seconds.
        float speed = activePuzzle.revealSeconds > 0f ? 1f / activePuzzle.revealSeconds : revealFadeSpeed;
        reveal = Mathf.MoveTowards(reveal, score, speed * Time.deltaTime);
        SetReveal(reveal);
    }

    // 0 = far from the hidden sound, 1 = right on it. Drives the static and the red colour.
    private void UpdateStatic()
    {
        float closeness = 0f;

        if (activePuzzle != null)
        {
            closeness = activePuzzle.Proximity(Mathf.Max(1f, NotchHz), PitchSemitones);

            if (Solved && activePuzzle.stayRevealedWhenSolved)
            {
                closeness = 1f;
            }
        }

        float target = Mathf.Pow(closeness, Mathf.Max(0.01f, staticCurve));

        staticAmount = Mathf.MoveTowards(staticAmount, target, staticResponseSpeed * Time.deltaTime);
        colourAmount = Mathf.MoveTowards(colourAmount, closeness, colourChangeSpeed * Time.deltaTime);
    }

    // =========================================================
    // ENDING: ONE-SHOT, "LOOK AWAY", TV OFF
    // =========================================================

    private IEnumerator SolvedSequence()
    {
        inFinale = true;
        drag = Drag.None;

        // The tape and the hidden sound stop; only the one-shot and the message remain.
        DetachDsps();
        ReleaseInstance(ref playback, FMOD.Studio.STOP_MODE.IMMEDIATE);
        ReleaseInstance(ref hidden, FMOD.Studio.STOP_MODE.IMMEDIATE);

        float oneShotSeconds = PlaySolvedOneShot();

        DrawMessage(solvedMessage);

        float wait = solvedMessageSeconds > 0f ? solvedMessageSeconds : Mathf.Max(2f, oneShotSeconds);
        yield return new WaitForSeconds(wait);

        TurnOff();
    }

    // Plays the one-shot at the TV and returns its length in seconds (0 if unknown).
    private float PlaySolvedOneShot()
    {
        EventReference oneShot = solvedOneShot;

        if (oneShot.IsNull && !string.IsNullOrEmpty(solvedOneShotPath))
        {
            try
            {
                oneShot = new EventReference { Guid = RuntimeManager.PathToGUID(solvedOneShotPath) };
            }
            catch (Exception)
            {
                Debug.LogWarning("TVMiniDaw: FMOD event '" + solvedOneShotPath + "' not found. " +
                                 "Is it in a bank and are the banks built?");
                return 0f;
            }
        }

        if (oneShot.IsNull)
        {
            return 0f;
        }

        FMOD.Studio.EventInstance instance = RuntimeManager.CreateInstance(oneShot);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
        instance.getDescription(out FMOD.Studio.EventDescription description);
        description.getLength(out int ms);
        instance.start();
        instance.release(); // frees itself when it finishes

        return ms / 1000f;
    }

    // A black screen with one centred message in big pixel letters.
    private void DrawMessage(string message)
    {
        if (screenTexture == null)
        {
            return;
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = screenOffColor;
        }

        string text = message.ToUpperInvariant();

        // Shrink the letters if the message doesn't fit.
        int scale = solvedMessageScale;
        while (scale > 1 && TextWidth(text, scale) > textureWidth - 16)
        {
            scale--;
        }

        int x = (textureWidth - TextWidth(text, scale)) / 2;
        int y = (textureHeight - 7 * scale) / 2;
        DrawText(text, x, y, scale, solvedMessageColor);

        screenTexture.SetPixels32(pixels);
        screenTexture.Apply(false);
    }

    // The TV switches itself off: black screen, the cassette back in the player, and it stays off.
    private void TurnOff()
    {
        inFinale = false;
        IsTurnedOff = true;
        IsOpen = false;
        ejectedFrame = Time.frameCount;

        ShowBlackScreen();

        if (turnOffWithTV != null)
        {
            foreach (GameObject go in turnOffWithTV)
            {
                if (go != null) go.SetActive(false);
            }
        }

        if (casettePlayerInspect != null)
        {
            casettePlayerInspect.SetHeldVisible(true);
        }

        insertedCassette = null;

        Debug.Log("[TV Mini DAW] Haunted frequency found: \"" + solvedMessage + "\" shown, TV turned off.");

        onEjected.Invoke();
    }

    // =========================================================
    // LIFETIME
    // =========================================================

    private void Start()
    {
        if (casettePlayerInspect == null)
        {
            casettePlayerInspect = FindFirstObjectByType<CasettePlayerInspect>();
        }

        // The quad stays visible from the start, as a black (switched off) screen.
        ShowBlackScreen();
    }

    private void Update()
    {
        if (!IsOpen || inFinale)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Eject();
            return;
        }

        HandleInput();
        UpdateLoop();
        UpdateDsps();
        ApplyParameters();
        UpdatePuzzle();

        // Solved this frame: the ending has taken over the screen.
        if (inFinale)
        {
            return;
        }

        UpdateStatic();
        DrawScreen();
    }

    private void OnDisable()
    {
        Eject();
    }

    private void OnDestroy()
    {
        if (screenTexture != null)
        {
            Destroy(screenTexture);
        }
    }

    // =========================================================
    // SCREEN
    // =========================================================

    // Creates the texture once, puts it on the quad's material and finds the collider.
    private void PrepareScreen()
    {
        if (screenTexture == null)
        {
            screenTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            screenTexture.wrapMode = TextureWrapMode.Clamp;
            screenTexture.filterMode = FilterMode.Point; // crunchy pixels suit the retro look
            pixels = new Color32[textureWidth * textureHeight];
        }

        if (dawScreen == null)
        {
            Debug.LogWarning("TVMiniDaw: No DAW screen assigned.");
            return;
        }

        if (!dawScreen.activeSelf)
        {
            dawScreen.SetActive(true);
        }

        if (screenCollider == null)
        {
            screenCollider = dawScreen.GetComponent<MeshCollider>();

            if (screenCollider == null)
            {
                Debug.LogWarning("TVMiniDaw: The DAW screen needs a MeshCollider for mouse control.");
            }
        }

        if (screenTextureAssigned)
        {
            return;
        }

        Renderer screenRenderer = dawScreen.GetComponent<Renderer>();

        if (screenRenderer != null)
        {
            Material material = screenRenderer.material;
            string property = FindTextureProperty(material);

            if (property != null)
            {
                material.SetTexture(property, screenTexture);
                screenTextureAssigned = true;
            }
            else
            {
                Debug.LogWarning("TVMiniDaw: The DAW screen material has no texture slot called '" +
                                 screenTextureProperty + "'.");
            }
        }
    }

    private string FindTextureProperty(Material material)
    {
        string[] candidates = { screenTextureProperty, "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex" };

        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && material.HasProperty(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // The TV is "off": a plain black screen, and the quad's collider is disabled
    // so it doesn't catch clicks meant for the TV.
    private void ShowBlackScreen()
    {
        PrepareScreen();

        if (screenCollider != null)
        {
            screenCollider.enabled = false;
        }

        if (screenTexture == null)
        {
            return;
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = screenOffColor;
        }

        screenTexture.SetPixels32(pixels);
        screenTexture.Apply(false);
    }

    // The cassette is in: draw the DAW and let the mouse use the quad.
    private void SetupScreen()
    {
        PrepareScreen();

        if (screenCollider != null)
        {
            screenCollider.enabled = true;
        }

        DrawScreen();
    }

    private void DrawScreen()
    {
        if (screenTexture == null)
        {
            return;
        }

        int w = textureWidth;
        int h = textureHeight;
        int padBottom = PadBottom;

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
        }

        // 1) The live spectrum as filled columns, on the same frequency axis as the notch.
        //    Near the hidden sound it shakes, distorts and slowly turns red.
        float amount = staticAmount;
        Color32 fillColour = Color32.Lerp(waveformColor, closeWaveformColor, colourAmount);
        Color32 lineColour = Color32.Lerp(spectrumColor, closeWaveformColor, colourAmount);

        int shakeX = Mathf.RoundToInt(NoiseSigned() * maxShakePixels * amount);
        int shakeY = Mathf.RoundToInt(NoiseSigned() * maxShakePixels * 0.5f * amount);
        int padHeight = h - padBottom;

        if (spectrum.Length > 0)
        {
            float nyquist = outputRate * 0.5f;

            for (int x = 0; x < w; x++)
            {
                int source = Mathf.Clamp(x - shakeX, 0, w - 1);
                float hz = Mathf.Max(1f, ColumnHz(source));
                int bin = Mathf.Clamp((int)(hz / nyquist * spectrum.Length), 0, spectrum.Length - 1);
                float db = 20f * Mathf.Log10(spectrum[bin] + 1e-6f);
                int jitter = Mathf.RoundToInt(NoiseSigned() * maxWaveJitterPixels * amount * amount);
                int top = padBottom + shakeY + jitter + Mathf.RoundToInt(Mathf.Clamp01((db + 80f) / 80f) * (padHeight - 1));

                for (int y = padBottom; y < top; y++)
                {
                    PutPad(x, y, fillColour, padBottom);
                }

                PutPad(x, top, lineColour, padBottom);
                PutPad(x, top - 1, lineColour, padBottom);

                // Ghost copy, like a bad signal.
                if (amount > 0.3f && Noise01() < amount * 0.5f)
                {
                    PutPad(x, top + Mathf.RoundToInt(amount * 12f), lineColour, padBottom);
                }
            }
        }

        // Static on top of the picture (the controls stay readable above it).
        ApplyStatic(padBottom);

        // 2) Notch (vertical line) and pitch (horizontal line).
        int notchX = Mathf.RoundToInt(notch01 * (w - 1));

        for (int y = padBottom; y < h; y++)
        {
            Put(notchX - 1, y, notchColor);
            Put(notchX, y, notchColor);
            Put(notchX + 1, y, notchColor);
        }

        int pitchY = padBottom + Mathf.RoundToInt(pitch01 * (padHeight - 1));

        for (int x = 0; x < w; x++)
        {
            Put(x, pitchY, pitchColor);
        }

        DrawReadouts();
        DrawTransport();

        screenTexture.SetPixels32(pixels);
        screenTexture.Apply(false);
    }

    // Frequency shown at a pixel column.
    private float ColumnHz(int x)
    {
        return DisplayHz(x / (float)(textureWidth - 1));
    }

    // PITCH and FREQUENCY, just above the slider.
    private void DrawReadouts()
    {
        int top = transportHeight + readoutHeight;
        FillRect(new RectInt(0, transportHeight, textureWidth, readoutHeight), panelColor);
        FillRect(new RectInt(0, top - 1, textureWidth, 1), sliderTrackColor);

        int scale = readoutTextScale;
        int y = transportHeight + (readoutHeight - 7 * scale) / 2;

        string pitchText = "PITCH: " + PitchSemitones.ToString(pitchStep >= 1f ? "+0;-0;0" : "+0.0;-0.0;0.0");
        string frequencyText = "FREQUENCY: " + Mathf.RoundToInt(NotchHz) + " HZ";

        DrawText(pitchText, 8, y, scale, pitchColor);
        DrawText(frequencyText, textureWidth - 8 - TextWidth(frequencyText, scale), y, scale, notchColor);
    }

    // Play / pause button, position slider and time, at the bottom.
    private void DrawTransport()
    {
        FillRect(new RectInt(0, 0, textureWidth, transportHeight), panelColor);

        // Play / pause.
        RectInt b = PlayButtonRect;

        if (paused)
        {
            // Play triangle, pointing right.
            for (int x = 0; x < b.width; x++)
            {
                int half = Mathf.RoundToInt((1f - x / (float)b.width) * b.height * 0.5f);
                int centre = b.y + b.height / 2;

                for (int y = centre - half; y <= centre + half; y++)
                {
                    Put(b.x + x, y, textColor);
                }
            }
        }
        else
        {
            // Pause bars.
            int barWidth = Mathf.Max(2, b.width / 3);
            FillRect(new RectInt(b.x, b.y, barWidth, b.height), textColor);
            FillRect(new RectInt(b.xMax - barWidth, b.y, barWidth, b.height), textColor);
        }

        // Slider.
        RectInt s = SliderRect;
        float position01 = lengthMs > 0 ? PositionMs() / (float)lengthMs : 0f;
        int fill = Mathf.RoundToInt(position01 * s.width);

        FillRect(s, sliderTrackColor);
        FillRect(new RectInt(s.x, s.y, fill, s.height), sliderFillColor);

        // Handle.
        FillRect(new RectInt(s.x + fill - 2, s.y - 5, 5, s.height + 10), textColor);

        // Time.
        string time = FormatTime(PositionMs()) + "/" + FormatTime(lengthMs);
        DrawText(time, textureWidth - 8 - TextWidth(time, 1), (transportHeight - 7) / 2, 1, textColor);
    }

    private static string FormatTime(int ms)
    {
        int seconds = Mathf.Max(0, ms) / 1000;
        return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    private void FillRect(RectInt r, Color32 color)
    {
        for (int y = r.yMin; y < r.yMax; y++)
        {
            for (int x = r.xMin; x < r.xMax; x++)
            {
                Put(x, y, color);
            }
        }
    }

    // VHS-style row tearing, a rolling bright band and snow over the XY pad. All scale with staticAmount.
    private void ApplyStatic(int padBottom)
    {
        float amount = staticAmount;

        if (amount <= 0.001f)
        {
            return;
        }

        int w = textureWidth;
        int h = textureHeight;

        // Row tearing: bands of rows slide sideways.
        int y = padBottom;

        while (y < h)
        {
            int band = 1 + (int)(Noise01() * 12f);
            int offset = Noise01() < amount * 0.6f
                ? Mathf.RoundToInt(NoiseSigned() * maxRowTearPixels * amount)
                : 0;

            for (int row = y; row < Mathf.Min(y + band, h) && offset != 0; row++)
            {
                ShiftRow(PixelRow(row), offset);
            }

            y += band;
        }

        // Rolling bright band (the picture losing vertical hold).
        int padHeight = h - padBottom;
        int bandCentre = padBottom + (int)(Time.time * 90f) % padHeight;
        int bandHeight = Mathf.RoundToInt(6f + 20f * amount);
        byte lift = (byte)(70f * amount);

        for (int row = bandCentre - bandHeight; row <= bandCentre + bandHeight; row++)
        {
            int r = PixelRow(padBottom + (((row - padBottom) % padHeight) + padHeight) % padHeight);

            for (int x = 0; x < w; x++)
            {
                Color32 c = pixels[r * w + x];
                pixels[r * w + x] = new Color32(
                    (byte)Mathf.Min(255, c.r + lift),
                    (byte)Mathf.Min(255, c.g + lift),
                    (byte)Mathf.Min(255, c.b + lift), 255);
            }
        }

        // Snow.
        float snowChance = maxSnowAmount * amount;

        for (int row = padBottom; row < h; row++)
        {
            int r = PixelRow(row);

            for (int x = 0; x < w; x++)
            {
                if (Noise01() < snowChance)
                {
                    byte v = (byte)(Noise01() * 255f);
                    pixels[r * w + x] = new Color32(v, v, v, 255);
                }
            }
        }
    }

    // Texture row of a control-space row.
    private int PixelRow(int controlRow)
    {
        return invertVertical ? textureHeight - 1 - controlRow : controlRow;
    }

    private void ShiftRow(int row, int offset)
    {
        int w = textureWidth;

        if (rowBuffer == null || rowBuffer.Length != w)
        {
            rowBuffer = new Color32[w];
        }

        Array.Copy(pixels, row * w, rowBuffer, 0, w);

        for (int x = 0; x < w; x++)
        {
            int source = x - offset;
            pixels[row * w + x] = (source >= 0 && source < w) ? rowBuffer[source] : backgroundColor;
        }
    }

    // Like Put, but only inside the XY pad (so the shaking spectrum never draws over the controls).
    private void PutPad(int x, int y, Color32 color, int padBottom)
    {
        if (y >= padBottom)
        {
            Put(x, y, color);
        }
    }

    private void Put(int x, int y, Color32 color)
    {
        if (x < 0 || y < 0 || x >= textureWidth || y >= textureHeight)
        {
            return;
        }

        // Everything is drawn in control space (left = low Hz, bottom = transport)
        // and mirrored here, so the picture always lines up with the mouse.
        if (invertHorizontal) x = textureWidth - 1 - x;
        if (invertVertical) y = textureHeight - 1 - y;

        pixels[y * textureWidth + x] = color;
    }

    // Cheap fast random (xorshift) for per-pixel noise.
    private float Noise01()
    {
        noiseState ^= noiseState << 13;
        noiseState ^= noiseState >> 17;
        noiseState ^= noiseState << 5;
        return (noiseState & 0xFFFFFF) / 16777216f;
    }

    private float NoiseSigned()
    {
        return Noise01() * 2f - 1f;
    }

    // =========================================================
    // PIXEL FONT (5 x 7)
    // =========================================================

    private static int TextWidth(string text, int scale)
    {
        return text.Length == 0 ? 0 : (text.Length * 6 - 1) * scale;
    }

    // (x, y) is the bottom-left corner of the text, in control space.
    private void DrawText(string text, int x, int y, int scale, Color32 color)
    {
        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw);

            if (Glyphs.TryGetValue(c, out string[] rows))
            {
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (rows[row][col] != '#')
                        {
                            continue;
                        }

                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                Put(x + col * scale + sx, y + (6 - row) * scale + sy, color);
                            }
                        }
                    }
                }
            }

            x += 6 * scale;
        }
    }

    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
        { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
        { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
        { '3', new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." } },
        { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
        { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
        { '6', new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." } },
        { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
        { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
        { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." } },
        { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
        { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
        { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
        { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
        { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
        { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" } },
        { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'I', new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." } },
        { 'J', new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." } },
        { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
        { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
        { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
        { 'N', new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" } },
        { 'O', new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
        { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
        { 'Q', new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" } },
        { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
        { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
        { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
        { 'U', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
        { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
        { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." } },
        { 'X', new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" } },
        { 'Y', new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." } },
        { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
        { ':', new[] { ".....", "..#..", "..#..", ".....", "..#..", "..#..", "....." } },
        { '.', new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." } },
        { '/', new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." } },
        { '+', new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." } },
        { '-', new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." } },
        { ' ', new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." } },
    };
}
