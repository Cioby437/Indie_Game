using System;
using System.Collections;
using System.Runtime.InteropServices;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;

// Hold R: everything the game is playing through FMOD is recorded.
// Release R: the Scriptable_1 emitter is enabled, its programmer instrument plays
// the recording, and the emitter is disabled again when the recording ends.
// Afterwards, P plays the recording again.
//
// Runs before other scripts so the emitter is disabled before its OnEnable
// can start Scriptable_1 without a recording.
[DefaultExecutionOrder(-200)]
public class Recorder_Script : MonoBehaviour
{

    [Header("Recording")]
    [Tooltip("Recording stops by itself after this many seconds, even if R is still held.")]
    public float maxRecordSeconds = 30f;

    [Header("FMOD Playback Emitter")]
    [Tooltip("The FMOD Event Emitter on the player that plays Scriptable_1. " +
             "Emitter settings: Play Event = Object Enable, Stop Event = Object Disable. " +
             "Must not be on the same GameObject as this script.")]
    public StudioEventEmitter playbackEmitter;
    [Header("FMOD Bank")]
    [Tooltip("The bank that Scriptable_1 is assigned to in FMOD Studio. Loaded when this script starts.")]
    [BankRef]
    public string recorderBank = "Recorder";

    [Header("Gameplay")]
    [Tooltip("Optional. When assigned, R and P only work while the Casette Player has been picked up " +
             "(it sits in the bottom right corner). Leave empty to allow recording at any time.")]
    public CasettePlayerInspect recorderHolder;

    // Kept in static fields so the delegates are never garbage collected.
    private static readonly FMOD.DSP_READ_CALLBACK CaptureReadCallback = OnCaptureRead;
    private static readonly EVENT_CALLBACK ProgrammerSoundCallback = OnProgrammerSound;

    // FMOD's callbacks are static, so they reach the recorder through this.
    private static Recorder_Script current;

    private bool bankLoaded = false;

    // Number of mix blocks the capture callback has seen during the current recording.
    private volatile int captureBlockCount = 0;
    private FMOD.DSP captureDSP;
    private float[] captureScratch;
    private int channels;
    private bool hasPlayedOnce = false;
    private volatile bool isRecording = false;
    private FMOD.ChannelGroup masterGroup;
    private EventDescription playbackDescription;
    private Coroutine playbackRoutine;

    // Preallocated so nothing is allocated on FMOD's mixer thread.
    private float[] recordBuffer;
    private volatile int recordWriteIndex = 0;

    // Channel count of the recording, taken from the first captured block.
    private volatile int recordedChannels = 0;
    private float recordedLength = 0f;
    private int recordedSampleCount = 0;
    private FMOD.Sound recordedSound;
    private int recordedSoundChannels = 0;
    private int sampleRate;

    public bool HasRecording
    {
        get { return recordedSound.hasHandle(); }
    }

    // =========================================================
    // PUBLIC API (used by the TV mini DAW)
    // =========================================================

    // While true, R and P do nothing (e.g. while the cassette is in the TV).
    public bool InputLocked { get; set; }

    public bool IsRecordingNow
    {
        get { return isRecording; }
    }

    // Scriptable_1. Any instance of it plays the recording (the callback is on the event description).
    public EventReference PlaybackEvent
    {
        get { return playbackEmitter != null ? playbackEmitter.EventReference : default(EventReference); }
    }

    public float RecordedLength
    {
        get { return recordedLength; }
    }

    // Fills min/max (one entry per column, -1..1) with the waveform of the current recording.
    public bool GetWaveform(float[] min, float[] max)
    {
        if (!HasRecording || recordedSampleCount == 0 || recordedSoundChannels == 0 ||
            min == null || max == null || min.Length != max.Length || min.Length == 0)
        {
            return false;
        }

        int frames = recordedSampleCount / recordedSoundChannels;
        int columns = min.Length;
        int framesPerColumn = Mathf.Max(1, frames / columns);

        for (int x = 0; x < columns; x++)
        {
            int start = x * framesPerColumn;
            int end = Mathf.Min(start + framesPerColumn, frames);
            float lo = 0f, hi = 0f;

            for (int f = start; f < end; f++)
            {
                float sum = 0f;

                for (int c = 0; c < recordedSoundChannels; c++)
                {
                    sum += recordBuffer[f * recordedSoundChannels + c];
                }

                float v = sum / recordedSoundChannels;
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
            }

            min[x] = lo;
            max[x] = hi;
        }

        return true;
    }

    public void StopPlayback()
    {
        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
            playbackRoutine = null;
        }

        if (playbackEmitter != null)
        {
            playbackEmitter.gameObject.SetActive(false);
        }

        // A recording that was cut off can still be replayed with P.
        if (recordedSound.hasHandle())
        {
            hasPlayedOnce = true;
        }
    }

    // Runs on FMOD's mixer thread, only while recording (the DSP is bypassed otherwise).
    // Copies the master output into the recording and passes it through untouched.
    [AOT.MonoPInvokeCallback(typeof(FMOD.DSP_READ_CALLBACK))]
    private static FMOD.RESULT OnCaptureRead(
        ref FMOD.DSP_STATE dspState,
        IntPtr inBuffer,
        IntPtr outBuffer,
        uint length,
        int inChannels,
        ref int outChannels
    )
    {
        Recorder_Script recorder = current;

        int sampleCount = (int)length * inChannels;

        if (recorder == null || sampleCount > recorder.captureScratch.Length)
        {
            return FMOD.RESULT.OK;
        }

        int writeIndex = recorder.recordWriteIndex;

        if (recorder.isRecording)
        {
            recorder.captureBlockCount++;

            // The first block decides the recording's channel count.
            if (recorder.recordedChannels == 0)
            {
                recorder.recordedChannels = inChannels;
            }
        }

        bool canRecord =
            recorder.isRecording &&
            inChannels == recorder.recordedChannels &&
            writeIndex + sampleCount <= recorder.recordBuffer.Length;

        if (canRecord)
        {
            // Straight into the recording, then from there to the output.
            Marshal.Copy(inBuffer, recorder.recordBuffer, writeIndex, sampleCount);
            Marshal.Copy(recorder.recordBuffer, writeIndex, outBuffer, sampleCount);

            recorder.recordWriteIndex = writeIndex + sampleCount;
        }
        else
        {
            Marshal.Copy(inBuffer, recorder.captureScratch, 0, sampleCount);
            Marshal.Copy(recorder.captureScratch, 0, outBuffer, sampleCount);
        }

        return FMOD.RESULT.OK;
    }

    // Hands the recorded sound to the programmer instrument in Scriptable_1.
    // Set on the event description, so it is in place before any instance starts.
    [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
    private static FMOD.RESULT OnProgrammerSound(
        EVENT_CALLBACK_TYPE type,
        IntPtr instancePtr,
        IntPtr parameterPtr
    )
    {
        if (type != EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND)
        {
            // DESTROY_PROGRAMMER_SOUND: the sound is kept so P can replay it.
            return FMOD.RESULT.OK;
        }

        Recorder_Script recorder = current;

        if (recorder == null || !recorder.recordedSound.hasHandle())
        {
            return FMOD.RESULT.OK;
        }

        PROGRAMMER_SOUND_PROPERTIES properties =
            Marshal.PtrToStructure<PROGRAMMER_SOUND_PROPERTIES>(parameterPtr);

        properties.sound = recorder.recordedSound.handle;
        properties.subsoundIndex = -1;

        Marshal.StructureToPtr(properties, parameterPtr, false);

        return FMOD.RESULT.OK;
    }

    private void Awake()
    {
        current = this;

        // The emitter must start disabled so enabling it triggers the event.
        if (playbackEmitter != null)
        {
            playbackEmitter.gameObject.SetActive(false);
        }

        // The playback event (Scriptable_1) lives in the Recorder bank.
        if (!string.IsNullOrEmpty(recorderBank))
        {
            try
            {
                RuntimeManager.LoadBank(recorderBank);
                bankLoaded = true;
            }
            catch (BankLoadException exception)
            {
                Debug.LogWarning(
                    "Recorder_Script: Could not load the '" + recorderBank + "' bank. " +
                    "Is it built in FMOD Studio? " + exception.Message
                );
            }
        }

        // Every Scriptable_1 instance (including the emitter's) gets the callback.
        if (playbackEmitter != null && !playbackEmitter.EventReference.IsNull)
        {
            playbackDescription = RuntimeManager.GetEventDescription(playbackEmitter.EventReference);

            playbackDescription.setCallback(
                ProgrammerSoundCallback,
                EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND | EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND
            );
        }
        else
        {
            Debug.LogWarning(
                "Recorder_Script: No playback emitter with an event (Scriptable_1) assigned."
            );
        }

        CreateCaptureDSP();
    }

    private void CreateCaptureDSP()
    {
        RuntimeManager.CoreSystem.getSoftwareFormat(out sampleRate, out FMOD.SPEAKERMODE speakerMode, out _);
        RuntimeManager.CoreSystem.getSpeakerModeChannels(speakerMode, out channels);
        RuntimeManager.CoreSystem.getDSPBufferSize(out uint bufferLength, out _);

        // Room for up to 32 channels per mix block.
        captureScratch = new float[bufferLength * 32];
        recordBuffer = new float[Mathf.CeilToInt(maxRecordSeconds * sampleRate) * channels];

        FMOD.DSP_DESCRIPTION description = new FMOD.DSP_DESCRIPTION();
        description.numinputbuffers = 1;
        description.numoutputbuffers = 1;
        description.read = CaptureReadCallback;

        if (RuntimeManager.CoreSystem.getMasterChannelGroup(out masterGroup) != FMOD.RESULT.OK ||
            RuntimeManager.CoreSystem.createDSP(ref description, out captureDSP) != FMOD.RESULT.OK ||
            masterGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, captureDSP) != FMOD.RESULT.OK)
        {
            Debug.LogWarning(
                "Recorder_Script: Could not attach the capture DSP to the master bus."
            );

            return;
        }

        // Bypassed DSPs pass audio through without running the callback,
        // so the mixer does no extra work while not recording.
        captureDSP.setBypass(true);
    }

    // Turns the captured samples into an FMOD sound.
    private bool CreateRecordedSound(int sampleCount, int recordingChannels)
    {
        if (sampleCount == 0 || recordingChannels == 0)
        {
            Debug.LogWarning(
                "Recorder_Script: Nothing was recorded. " +
                "Capture blocks seen: " + captureBlockCount +
                ", block channels: " + recordingChannels +
                ", mixer channels: " + channels +
                ", sample rate: " + sampleRate +
                (captureBlockCount == 0
                    ? ". The capture DSP never ran - it may not be attached to the master bus."
                    : ".")
            );

            return false;
        }

        ReleaseRecordedSound();

        FMOD.CREATESOUNDEXINFO exInfo = new FMOD.CREATESOUNDEXINFO();
        exInfo.cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO));
        exInfo.numchannels = recordingChannels;
        exInfo.defaultfrequency = sampleRate;
        exInfo.format = FMOD.SOUND_FORMAT.PCMFLOAT;
        exInfo.length = (uint)(sampleCount * sizeof(float));

        FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(
            "",
            FMOD.MODE.OPENUSER | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF,
            ref exInfo,
            out recordedSound
        );

        if (result != FMOD.RESULT.OK)
        {
            Debug.LogWarning(
                "Recorder_Script: Could not create the recorded sound (" + result + ")."
            );

            return false;
        }

        recordedSound.@lock(0, exInfo.length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2);

        int firstCount = (int)(len1 / sizeof(float));

        Marshal.Copy(recordBuffer, 0, ptr1, firstCount);

        if (len2 > 0)
        {
            Marshal.Copy(recordBuffer, firstCount, ptr2, (int)(len2 / sizeof(float)));
        }

        recordedSound.unlock(ptr1, ptr2, len1, len2);

        recordedLength = (float)sampleCount / recordingChannels / sampleRate;
        recordedSampleCount = sampleCount;
        recordedSoundChannels = recordingChannels;

        return true;
    }

    private bool IsPlaying()
    {
        return playbackRoutine != null;
    }

    private void OnDestroy()
    {
        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
            playbackRoutine = null;
        }

        if (playbackDescription.isValid())
        {
            playbackDescription.setCallback(null, 0);
        }

        if (current == this)
        {
            current = null;
        }

        ReleaseRecordedSound();

        if (captureDSP.hasHandle())
        {
            masterGroup.removeDSP(captureDSP);
            captureDSP.release();
        }

        if (bankLoaded)
        {
            RuntimeManager.UnloadBank(recorderBank);
        }
    }

    private void PlayRecording()
    {
        if (!recordedSound.hasHandle() || playbackEmitter == null)
        {
            return;
        }

        StopPlayback();

        playbackRoutine = StartCoroutine(PlaybackRoutine());
    }

    // Enables the emitter and disables it again when the recording ends.
    private IEnumerator PlaybackRoutine()
    {
        // Enabling the object makes the emitter start Scriptable_1 (Play On: Object Enable).
        playbackEmitter.gameObject.SetActive(true);

        // FMOD plays in real time, regardless of Time.timeScale.
        yield return new WaitForSecondsRealtime(recordedLength);

        // Disabling the object stops the event (Stop On: Object Disable).
        playbackEmitter.gameObject.SetActive(false);

        playbackRoutine = null;
        hasPlayedOnce = true;
    }

    private void ReleaseRecordedSound()
    {
        if (recordedSound.hasHandle())
        {
            Debug.Log("[Recorder] Previous recording released (a new one replaces it).");
        }

        // Releasing the sound also stops it wherever it is still playing.
        if (recordedSound.hasHandle())
        {
            recordedSound.release();
            recordedSound.clearHandle();
        }
    }

    private void StartRecording()
    {
        if (!captureDSP.hasHandle())
        {
            return;
        }

        recordWriteIndex = 0;
        recordedChannels = 0;
        captureBlockCount = 0;
        isRecording = true;

        Debug.Log("[Recorder] Recording started.");

        captureDSP.setBypass(false);
    }

    private void StopRecording()
    {
        isRecording = false;

        if (captureDSP.hasHandle())
        {
            captureDSP.setBypass(true);
        }

        // Only whole samples written before recording stopped are used.
        int sampleCount = recordWriteIndex;

        // Stop the old playback before its sound is replaced.
        StopPlayback();

        if (CreateRecordedSound(sampleCount, recordedChannels))
        {
            Debug.Log("[Recorder] Recording saved: " + recordedLength.ToString("0.00") + " s, " +
                      recordedSoundChannels + " channel(s), FMOD sound valid: " + recordedSound.hasHandle() + ".");

            hasPlayedOnce = false;

            PlayRecording();
        }
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        // A recording in progress can always be finished, even if input gets locked meanwhile.
        bool canUseRecorder =
            !InputLocked &&
            (recorderHolder == null || recorderHolder.IsHeld);

        // =========================================================
        // R - RECORD WHILE HELD, PLAY BACK ON RELEASE
        // =========================================================

        if (canUseRecorder && Keyboard.current.rKey.wasPressedThisFrame)
        {
            StartRecording();
        }
        else if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Debug.Log("[Recorder] R ignored: " +
                      (InputLocked ? "input is locked (cassette is in the TV)." :
                                     "the Casette Player hasn't been picked up yet."));
        }

        if (isRecording && Keyboard.current.rKey.wasReleasedThisFrame)
        {
            StopRecording();
        }

        // =========================================================
        // P - PLAY THE RECORDING AGAIN
        // =========================================================

        if (canUseRecorder &&
            hasPlayedOnce &&
            !isRecording &&
            !IsPlaying() &&
            Keyboard.current.pKey.wasPressedThisFrame)
        {
            PlayRecording();
        }
    }
}