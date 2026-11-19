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
             "It stays black until the cassette is inserted; its collider is only on while the DAW is open.")]
    public GameObject dawScreen;

    [Tooltip("Colour of the screen while no cassette is inserted.")]
    public Color32 screenOffColor = new Color32(0, 0, 0, 255);

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

    [Header("Static (gets worse near the hidden sound)")]
    [Tooltip("The waveform slowly turns this colour as the player gets close.")]
    public Color32 closeWaveformColor = new Color32(230, 25, 20, 255);
    [Tooltip("How fast the colour follows the player's closeness (0-1 per second). Low = slow drift to red.")]
    public float colourChangeSpeed = 0.35f;
    [Tooltip("How fast the static/shake follows the player's closeness (0-1 per second).")]
    public float staticResponseSpeed = 2.5f;
    [Tooltip("1 = static grows evenly; higher = barely any until close, then violent.")]
    public float staticCurve = 2f;
    [Tooltip("Max whole-waveform shake in pixels at full closeness.")]
    public float maxShakePixels = 18f;
    [Tooltip("Max random vertical jitter per waveform column, in pixels.")]
    public float maxWaveJitterPixels = 40f;
    [Tooltip("Max horizontal tearing of screen rows (VHS tracking), in pixels.")]
    public int maxRowTearPixels = 60;
    [Tooltip("Fraction of pixels turned to snow at full closeness.")]
    [Range(0f, 1f)] public float maxSnowAmount = 0.55f;
    [Tooltip("Optional FMOD parameter (0-1) on Scriptable_1 that receives the static amount, e.g. to fade in a static noise layer.")]
    public string staticParameter = "";
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
    private float staticAmount = 0f;    // 0-1, drives snow/shake/tearing
    private float colourAmount = 0f;    // 0-1, drives green -> red (slower)
    private uint noiseState = 2463534242u;
    private MeshCollider screenCollider;

    private Texture2D screenTexture;
    private bool screenTextureAssigned;
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

    // Why the cassette can't go in right now (null = it can). Logged when the TV is clicked.
    public string InsertBlocker
    {
        get
        {
            if (IsOpen) return "the cassette is already in the TV.";
            if (recorder == null) return "no Recorder is assigned on TVMiniDaw.";
            if (casettePlayerInspect != null && !casettePlayerInspect.IsHeld) return "the Casette Player hasn't been picked up from the drawer yet.";
            if (recorder.IsRecordingNow) return "the recorder is still recording (release R first).";
            if (!recorder.HasRecording) return "nothing has been recorded yet (hold R, then release).";
            if (requireDialogueFinished && dialogueManager != null && !dialogueManager.AllDialogueTraversed) return "the TV dialogue isn't finished yet (Require Dialogue Finished is ticked).";
            return null;
        }
    }

    // Full state report for debugging the insert step.
    public string DebugStatus()
    {
        string recorderInfo = recorder == null
            ? "  Recorder: NOT ASSIGNED on TVMiniDaw\n"
            : "  Recorder: " + recorder.name + "\n" +
              "  Has recording (FMOD sound saved): " + recorder.HasRecording + "\n" +
              "  Recorded length: " + recorder.RecordedLength.ToString("0.00") + " s\n" +
              "  Recording right now: " + recorder.IsRecordingNow + "\n" +
              "  Recorder input locked: " + recorder.InputLocked + "\n" +
              "  Playback event: " + (recorder.PlaybackEvent.IsNull ? "NONE" : recorder.PlaybackEvent.ToString()) + "\n";

        string holderInfo = casettePlayerInspect == null
            ? "  Casette Player Inspect: not assigned (pick-up not required)\n"
            : "  Casette Player picked up (held): " + casettePlayerInspect.IsHeld + "\n";

        string dialogueInfo = dialogueManager == null
            ? "  Dialogue Manager: not assigned on TVMiniDaw\n"
            : "  Dialogue active: " + dialogueManager.IsDialogueActive +
              ", all dialogue traversed: " + dialogueManager.AllDialogueTraversed +
              ", require finished: " + requireDialogueFinished + "\n";

        return "[TV Mini DAW] Status:\n" +
               recorderInfo + holderInfo + dialogueInfo +
               "  DAW screen: " + (dawScreen == null ? "NOT ASSIGNED" : dawScreen.name) + "\n" +
               "  Already open: " + IsOpen + "\n" +
               "  Can insert: " + CanInsert + (InsertBlocker != null ? " (" + InsertBlocker + ")" : "");
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

        // Back to a black screen. The quad stays on; only its collider turns off,
        // so clicks reach the TV behind it again.
        ShowBlackScreen();

        if (recorder != null)
        {
            recorder.InputLocked = false;
        }

        if (casettePlayerInspect != null)
        {
            casettePlayerInspect.SetHeldVisible(true);
        }

        Debug.Log("[TV Mini DAW] Cassette ejected.");

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
        staticAmount = 0f;
        colourAmount = 0f;
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
        if (puzzle != null && !UsesSeparateHiddenEvent())
        {
            SetReveal(Solved && puzzle.stayRevealedWhenSolved ? 1f : 0f);
        }

        playback.start();

        outputRate = 48000;
        RuntimeManager.CoreSystem.getSoftwareFormat(out outputRate, out _, out _);

        if (puzzle != null && UsesSeparateHiddenEvent())
        {
            hidden = RuntimeManager.CreateInstance(puzzle.hiddenEvent);
            hidden.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
            SetReveal(Solved && puzzle.stayRevealedWhenSolved ? 1f : 0f);
            hidden.start();
        }

        Debug.Log("[TV Mini DAW] Cassette inserted. DAW screen on, recording looping at the TV.");

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

        // 1) The recording's waveform (time axis). Near the hidden sound it shakes,
        //    distorts and slowly turns red.
        int middle = h / 2;
        float amount = staticAmount;
        Color32 waveColour = Color32.Lerp(waveformColor, closeWaveformColor, colourAmount);

        int shakeX = Mathf.RoundToInt(NoiseSigned() * maxShakePixels * amount);
        int shakeY = Mathf.RoundToInt(NoiseSigned() * maxShakePixels * 0.5f * amount);
        float ampWobble = 1f + NoiseSigned() * 0.6f * amount;

        for (int x = 0; x < w; x++)
        {
            int source = Mathf.Clamp(x - shakeX, 0, w - 1);
            int jitter = Mathf.RoundToInt(NoiseSigned() * maxWaveJitterPixels * amount * amount);

            int y0 = middle + shakeY + jitter + Mathf.RoundToInt(waveMin[source] * ampWobble * (h / 2 - 1));
            int y1 = middle + shakeY + jitter + Mathf.RoundToInt(waveMax[source] * ampWobble * (h / 2 - 1));

            for (int y = y0; y <= y1; y++)
            {
                Put(x, y, waveColour);
            }

            // Ghost copy, like a bad signal.
            if (amount > 0.3f && Noise01() < amount * 0.5f)
            {
                int ghost = Mathf.RoundToInt(amount * 12f);
                Put(x, y0 - ghost, waveColour);
                Put(x, y1 + ghost, waveColour);
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

        // Static on top of the picture (the control lines stay readable above it).
        ApplyStatic();

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

    // VHS-style row tearing, a rolling bright band and snow. All scale with staticAmount.
    private void ApplyStatic()
    {
        float amount = staticAmount;

        if (amount <= 0.001f)
        {
            return;
        }

        int w = textureWidth;
        int h = textureHeight;

        // Row tearing: bands of rows slide sideways.
        int y = 0;

        while (y < h)
        {
            int band = 1 + (int)(Noise01() * 12f);
            int offset = Noise01() < amount * 0.6f
                ? Mathf.RoundToInt(NoiseSigned() * maxRowTearPixels * amount)
                : 0;

            for (int row = y; row < Mathf.Min(y + band, h) && offset != 0; row++)
            {
                ShiftRow(row, offset);
            }

            y += band;
        }

        // Rolling bright band (the picture losing vertical hold).
        int bandCentre = (int)(Time.time * 90f) % h;
        int bandHeight = Mathf.RoundToInt(6f + 20f * amount);

        for (int row = bandCentre - bandHeight; row <= bandCentre + bandHeight; row++)
        {
            int r = ((row % h) + h) % h;
            byte lift = (byte)(70f * amount);

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

        for (int i = 0; i < pixels.Length; i++)
        {
            if (Noise01() < snowChance)
            {
                byte v = (byte)(Noise01() * 255f);
                pixels[i] = new Color32(v, v, v, 255);
            }
        }
    }

    private Color32[] rowBuffer;

    private void ShiftRow(int row, int offset)
    {
        int w = textureWidth;

        if (rowBuffer == null || rowBuffer.Length != w)
        {
            rowBuffer = new Color32[w];
        }

        System.Array.Copy(pixels, row * w, rowBuffer, 0, w);

        for (int x = 0; x < w; x++)
        {
            int source = x - offset;
            pixels[row * w + x] = (source >= 0 && source < w) ? rowBuffer[source] : backgroundColor;
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

    // Creates the texture once, puts it on the quad's material and finds the collider.
    private void PrepareScreen()
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

        if (screenTexture == null)
        {
            return;
        }

        recorder.GetWaveform(waveMin, waveMax);
        DrawScreen();
    }

    // =========================================================
    // LIFETIME
    // =========================================================

    private void Start()
    {
        // The quad stays visible from the start, as a black (switched off) screen.
        ShowBlackScreen();
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
        UpdateStatic();
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

    // Option B only applies to a *different* event. If the puzzle points at Scriptable_1 itself,
    // a second copy of the recording would play, so it's treated as Option A (track inside Scriptable_1).
    private bool UsesSeparateHiddenEvent()
    {
        if (puzzle == null || puzzle.hiddenEvent.IsNull)
        {
            return false;
        }

        if (recorder != null && !recorder.PlaybackEvent.IsNull &&
            puzzle.hiddenEvent.Guid.Equals(recorder.PlaybackEvent.Guid))
        {
            if (!warnedSameHiddenEvent)
            {
                Debug.LogWarning("[TV Mini DAW] The puzzle's Hidden Event is Scriptable_1 itself. It is ignored: " +
                                 "the hidden track inside Scriptable_1 is revealed with the '" + puzzle.revealParameter +
                                 "' parameter. Clear the Hidden Event field on the puzzle asset.");
                warnedSameHiddenEvent = true;
            }

            return false;
        }

        return true;
    }

    private bool warnedSameHiddenEvent;

    // 0 = far from the hidden sound, 1 = right on it. Drives the static and the red colour.
    private void UpdateStatic()
    {
        float closeness = 0f;

        if (puzzle != null)
        {
            closeness = puzzle.Proximity(NotchHz, PitchSemitones);

            if (Solved && puzzle.stayRevealedWhenSolved)
            {
                closeness = 1f;
            }
        }

        float target = Mathf.Pow(closeness, Mathf.Max(0.01f, staticCurve));

        staticAmount = Mathf.MoveTowards(staticAmount, target, staticResponseSpeed * Time.deltaTime);
        colourAmount = Mathf.MoveTowards(colourAmount, closeness, colourChangeSpeed * Time.deltaTime);

        if (!string.IsNullOrEmpty(staticParameter) && playback.isValid())
        {
            playback.setParameterByName(staticParameter, staticAmount);
        }
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