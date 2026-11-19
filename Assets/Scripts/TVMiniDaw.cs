using System;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

// The cassette goes into the TV and the TV becomes a tiny DAW for the recording.
//
// Flow: CursorCameraManager calls Insert() when the TV is clicked (TV camera active)
// while the Casette Player is held and has a recording. Escape or the back button calls Eject().
//
// The DAW screen is an XY pad:
//   left / right = notch filter frequency (log scale, 20 Hz - 20 kHz)
//   up / down    = pitch bend
// Arrow keys nudge both (hold Shift for fine steps).
//
// The recording loops through its own Scriptable_1 instance placed at the TV, so the
// "NotchFreq" and "Pitch" parameters drive the EQ and pitch inside FMOD in real time.
// When they hit the target in the SecretLayerPuzzle, the hidden event fades in underneath.
public class TVMiniDaw : MonoBehaviour
{

    [Header("Screen Colours")]
    public Color32 backgroundColor = new Color32(4, 14, 10, 255);

    [Tooltip("Optional. Hides the held Casette Player while its cassette is in the TV.")]
    public CasettePlayerInspect casettePlayerInspect;

    [Header("DAW Screen")]
    [Tooltip("A Quad placed just in front of the TV glass (needs a MeshRenderer and a MeshCollider). " +
             "Keep it disabled: it is switched on while the cassette is inserted.")]
    public GameObject dawScreen;

    [Tooltip("Optional. Lets the TV finish its dialogue before the cassette can be inserted.")]
    public DialogueManager dialogueManager;

    [Tooltip("Flip these if dragging moves the controls the wrong way (depends on the quad's rotation).")]
    public bool invertHorizontal = false;
    public bool invertVertical = false;

    [Header("Controls")]
    public float keyboardSpeed = 0.25f;
    public float maxHz = 20000f;

    public float minHz = 20f;
    public Color32 notchColor = new Color32(255, 200, 60, 255);

    [Header("FMOD Parameters (on Scriptable_1)")]
    [Tooltip("Continuous, 0 to 1. Automates the notch band's frequency: 0 = minHz, 1 = maxHz.")]
    public string notchParameter = "NotchFreq";
    public UnityEvent onEjected;

    public UnityEvent onInserted;
    public UnityEvent onSolved;
    public Color32 pitchColor = new Color32(80, 160, 255, 255);

    [Tooltip("Continuous, -pitchRange to +pitchRange. Automates the event's Pitch macro (semitones).")]
    public string pitchParameter = "Pitch";
    public float pitchRangeSemitones = 12f;

    [Header("Puzzle")]
    public SecretLayerPuzzle puzzle;

    [Tooltip("Camera that renders the game (the one with the CinemachineBrain). Defaults to Camera.main.")]
    public Camera raycastCamera;
    [Header("References")]
    public Recorder_Script recorder;

    [Tooltip("When ticked, the cassette can only be inserted after all TV dialogue has been heard.")]
    public bool requireDialogueFinished = false;

    [Tooltip("How fast the hidden layer fades in / out (volume per second).")]
    public float revealFadeSpeed = 3f;

    [Tooltip("Texture slot on the DAW screen's material. HDRP Unlit: _UnlitColorMap.")]
    public string screenTextureProperty = "_UnlitColorMap";
    public Color32 spectrumColor = new Color32(90, 255, 160, 255);
    public int textureHeight = 256;

    public int textureWidth = 512;
    public Color32 waveformColor = new Color32(30, 90, 60, 255);
    private int ejectedFrame = -1;

    // FFT on the playback instance, so the spectrum only shows the recording (after the EQ).
    private DSP fft;
    private FMOD.ChannelGroup fftGroup;
    private FMOD.Studio.EventInstance hidden;
    private float holdTimer = 0f;
    private int insertedFrame = -1;

    // =========================================================
    // STATE
    // =========================================================

    private float notch01 = 0.5f;
    private int outputRate = 48000;
    private float pitch01 = 0.5f;
    private Color32[] pixels;

    private FMOD.Studio.EventInstance playback;
    private float reveal = 0f;
    private MeshCollider screenCollider;

    private Texture2D screenTexture;
    private float[] spectrum = new float[0];
    private float[] waveMax;
    private float[] waveMin;

    // CursorCameraManager ignores the mouse while this is true (also on the frame of ejecting).
    public bool BlocksInput
    {
        get { return IsOpen || ejectedFrame == Time.frameCount; }
    }

    public bool CanInsert
    {
        get
        {
            return !IsOpen &&
                   recorder != null &&
                   recorder.HasRecording &&
                   !recorder.IsRecordingNow &&
                   (casettePlayerInspect == null || casettePlayerInspect.IsHeld) &&
                   (!requireDialogueFinished || dialogueManager == null || dialogueManager.AllDialogueTraversed);
        }
    }

    public bool IsOpen { get; private set; }

    public float NotchHz
    {
        get { return minHz * Mathf.Pow(maxHz / minHz, notch01); }
    }

    public float PitchSemitones
    {
        get { return Mathf.Lerp(-pitchRangeSemitones, pitchRangeSemitones, pitch01); }
    }
    public bool Solved { get; private set; }

    public void Eject()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        ejectedFrame = Time.frameCount;

        DetachFft();

        if (playback.isValid())
        {
            playback.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            playback.release();
            playback.clearHandle();
        }

        if (hidden.isValid())
        {
            hidden.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            hidden.release();
            hidden.clearHandle();
        }

        if (dawScreen != null)
        {
            dawScreen.SetActive(false);
        }

        if (recorder != null)
        {
            recorder.InputLocked = false;
        }

        if (casettePlayerInspect != null)
        {
            casettePlayerInspect.SetHeldVisible(true);
        }

        onEjected.Invoke();
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

        if (recorder.PlaybackEvent.IsNull)
        {
            Debug.LogWarning("TVMiniDaw: The recorder has no playback event (Scriptable_1).");
            return;
        }

        IsOpen = true;
        insertedFrame = Time.frameCount;
        reveal = 0f;
        holdTimer = 0f;

        // The recorder is "inside" the TV now.
        recorder.StopPlayback();
        recorder.InputLocked = true;

        if (casettePlayerInspect != null)
        {
            casettePlayerInspect.SetHeldVisible(false);
        }

        SetupScreen();

        // Our own Scriptable_1 instance at the TV. The recorder's callback on the event
        // description hands it the recorded sound.
        playback = RuntimeManager.CreateInstance(recorder.PlaybackEvent);
        playback.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
        ApplyParameters();

        // Hidden layer as a track inside Scriptable_1: start it silent (or revealed if already solved).
        if (puzzle != null && puzzle.hiddenEvent.IsNull)
        {
            SetReveal(Solved && puzzle.stayRevealedWhenSolved ? 1f : 0f);
        }

        playback.start();

        outputRate = 48000;
        RuntimeManager.CoreSystem.getSoftwareFormat(out outputRate, out _, out _);

        if (puzzle != null && !puzzle.hiddenEvent.IsNull)
        {
            hidden = RuntimeManager.CreateInstance(puzzle.hiddenEvent);
            hidden.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
            SetReveal(Solved && puzzle.stayRevealedWhenSolved ? 1f : 0f);
            hidden.start();
        }

        onInserted.Invoke();
    }

    // Switch to another puzzle, e.g. from the ScenarioManager.
    public void SetPuzzle(SecretLayerPuzzle newPuzzle)
    {
        puzzle = newPuzzle;
        Solved = false;
        holdTimer = 0f;
    }

    private void ApplyParameters()
    {
        if (!playback.isValid())
        {
            return;
        }

        playback.setParameterByName(notchParameter, notch01);
        playback.setParameterByName(pitchParameter, PitchSemitones);
    }

    private void DetachFft()
    {
        if (fft.hasHandle())
        {
            // The old channel group may already be gone; errors here are harmless.
            if (fftGroup.hasHandle())
            {
                fftGroup.removeDSP(fft);
            }

            fft.release();
            fft.clearHandle();
        }

        fftGroup.clearHandle();
        spectrum = new float[0];
    }

    private void DrawScreen()
    {
        if (screenTexture == null)
        {
            return;
        }

        int w = textureWidth;
        int h = textureHeight;

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
        }

        // 1) The recording's waveform (time axis), dim in the background.
        int middle = h / 2;

        for (int x = 0; x < w; x++)
        {
            int y0 = middle + Mathf.RoundToInt(waveMin[x] * (h / 2 - 1));
            int y1 = middle + Mathf.RoundToInt(waveMax[x] * (h / 2 - 1));

            for (int y = y0; y <= y1; y++)
            {
                Put(x, y, waveformColor);
            }
        }

        // 2) Live spectrum, on the same log frequency axis as the notch.
        if (spectrum.Length > 0)
        {
            float nyquist = outputRate * 0.5f;

            for (int x = 0; x < w; x++)
            {
                float hz = minHz * Mathf.Pow(maxHz / minHz, x / (float)(w - 1));
                int bin = Mathf.Clamp((int)(hz / nyquist * spectrum.Length), 0, spectrum.Length - 1);
                float db = 20f * Mathf.Log10(spectrum[bin] + 1e-6f);
                int top = Mathf.RoundToInt(Mathf.Clamp01((db + 80f) / 80f) * (h - 1));

                Put(x, top, spectrumColor);
                Put(x, top - 1, spectrumColor);
            }
        }

        // 3) Notch (vertical line) and pitch (horizontal line).
        int notchX = Mathf.RoundToInt(notch01 * (w - 1));

        for (int y = 0; y < h; y++)
        {
            Put(notchX - 1, y, notchColor);
            Put(notchX, y, notchColor);
            Put(notchX + 1, y, notchColor);
        }

        int pitchY = Mathf.RoundToInt(pitch01 * (h - 1));

        for (int x = 0; x < w; x++)
        {
            Put(x, pitchY, pitchColor);
        }

        screenTexture.SetPixels32(pixels);
        screenTexture.Apply(false);
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

    private void HandleInput()
    {
        // Ignore the click that inserted the cassette.
        bool mouseHeld =
            Mouse.current != null &&
            Mouse.current.leftButton.isPressed &&
            Time.frameCount != insertedFrame &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());

        if (mouseHeld && screenCollider != null)
        {
            Camera cam = raycastCamera != null ? raycastCamera : Camera.main;

            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

                // Only the DAW screen is tested, so nothing else in the room can catch the click.
                if (screenCollider.Raycast(ray, out RaycastHit hit, 100f))
                {
                    Vector2 uv = hit.textureCoord;

                    notch01 = invertHorizontal ? 1f - uv.x : uv.x;
                    pitch01 = invertVertical ? 1f - uv.y : uv.y;
                }
            }
        }

        if (Keyboard.current != null)
        {
            float step = Time.deltaTime * keyboardSpeed *
                         (Keyboard.current.leftShiftKey.isPressed ? 0.15f : 1f);

            if (Keyboard.current.rightArrowKey.isPressed) notch01 += step;
            if (Keyboard.current.leftArrowKey.isPressed) notch01 -= step;
            if (Keyboard.current.upArrowKey.isPressed) pitch01 += step;
            if (Keyboard.current.downArrowKey.isPressed) pitch01 -= step;
        }

        notch01 = Mathf.Clamp01(notch01);
        pitch01 = Mathf.Clamp01(pitch01);
    }

    private void OnDestroy()
    {
        if (screenTexture != null)
        {
            Destroy(screenTexture);
        }
    }

    private void OnDisable()
    {
        Eject();
    }

    private void Put(int x, int y, Color32 color)
    {
        if (x < 0 || y < 0 || x >= textureWidth || y >= textureHeight)
        {
            return;
        }

        // Everything is drawn in control space (left = low Hz, bottom = low pitch)
        // and mirrored here, so the picture always lines up with the mouse.
        if (invertHorizontal) x = textureWidth - 1 - x;
        if (invertVertical) y = textureHeight - 1 - y;

        pixels[y * textureWidth + x] = color;
    }

    private void SetReveal(float value)
    {
        if (puzzle == null)
        {
            return;
        }

        // Option A (recommended): the hidden layer is a track inside Scriptable_1.
        // Its volume is automated by the reveal parameter (0 = silent, 1 = full).
        if (!hidden.isValid())
        {
            if (playback.isValid() && !string.IsNullOrEmpty(puzzle.revealParameter))
            {
                playback.setParameterByName(puzzle.revealParameter, value);
            }

            return;
        }

        // Option B: the hidden layer is a separate event.
        if (string.IsNullOrEmpty(puzzle.revealParameter))
        {
            hidden.setVolume(value);
        }
        else
        {
            hidden.setParameterByName(puzzle.revealParameter, value);
        }
    }

    // =========================================================
    // SCREEN
    // =========================================================

    private void SetupScreen()
    {
        if (screenTexture == null)
        {
            screenTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            screenTexture.wrapMode = TextureWrapMode.Clamp;
            screenTexture.filterMode = FilterMode.Point; // crunchy pixels suit the retro look
            pixels = new Color32[textureWidth * textureHeight];
            waveMin = new float[textureWidth];
            waveMax = new float[textureWidth];
        }

        recorder.GetWaveform(waveMin, waveMax);

        if (dawScreen == null)
        {
            Debug.LogWarning("TVMiniDaw: No DAW screen assigned.");
            return;
        }

        dawScreen.SetActive(true);
        screenCollider = dawScreen.GetComponent<MeshCollider>();

        if (screenCollider == null)
        {
            Debug.LogWarning("TVMiniDaw: The DAW screen needs a MeshCollider for mouse control.");
        }

        Renderer screenRenderer = dawScreen.GetComponent<Renderer>();

        if (screenRenderer != null)
        {
            Material material = screenRenderer.material;
            string property = FindTextureProperty(material);

            if (property != null)
            {
                material.SetTexture(property, screenTexture);
            }
            else
            {
                Debug.LogWarning("TVMiniDaw: The DAW screen material has no texture slot called '" +
                                 screenTextureProperty + "'.");
            }
        }

        DrawScreen();
    }

    // =========================================================
    // LIFETIME
    // =========================================================

    private void Start()
    {
        if (dawScreen != null)
        {
            dawScreen.SetActive(false);
        }
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Eject();
            return;
        }

        HandleInput();
        ApplyParameters();

        // Loop the recording while the cassette is in.
        playback.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state);

        if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            playback.start();
        }

        if (hidden.isValid())
        {
            hidden.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE hiddenState);

            if (hiddenState == FMOD.Studio.PLAYBACK_STATE.STOPPED)
            {
                hidden.start();
            }
        }

        UpdatePuzzle();
        UpdateFft();
        DrawScreen();
    }

    // =========================================================
    // SPECTRUM (FFT DSP on the playback instance)
    // =========================================================

    private void UpdateFft()
    {
        // The instance's channel group only exists once FMOD has started it,
        // and can change when the loop restarts, so it is checked every frame.
        if (playback.getChannelGroup(out FMOD.ChannelGroup group) != RESULT.OK || !group.hasHandle())
        {
            return;
        }

        if (group.handle != fftGroup.handle)
        {
            DetachFft();

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

            fftGroup = group;
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

    // =========================================================
    // PUZZLE
    // =========================================================

    private void UpdatePuzzle()
    {
        if (puzzle == null)
        {
            return;
        }

        float score = puzzle.Evaluate(NotchHz, PitchSemitones);

        if (Solved && puzzle.stayRevealedWhenSolved)
        {
            score = 1f;
        }

        if (!Solved)
        {
            if (score >= 1f)
            {
                holdTimer += Time.deltaTime;

                if (holdTimer >= puzzle.holdToSolveSeconds)
                {
                    Solved = true;
                    onSolved.Invoke();
                }
            }
            else
            {
                holdTimer = 0f;
            }
        }

        reveal = Mathf.MoveTowards(reveal, score, revealFadeSpeed * Time.deltaTime);
        SetReveal(reveal);
    }
}