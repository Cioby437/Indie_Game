using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

// "Closed eyes" and "eyes adjusting" effect for the cutscene.
//
// Three layers work together:
//  1. Eyelids: a full-screen overlay with an eye-shaped opening and soft edges (no art needed).
//  2. HDRP post-processing on its own high-priority Volume: depth of field (blurry vision /
//     focus on a distance), vignette, exposure (dark when closed, glare right after opening),
//     desaturation and chromatic aberration.
//  3. Your RetroLook shader: extra blur, vignette and colour fringing while vision is blurry.
//     Its original values are restored afterwards.
//
// The cutscene drives three values:
//   openness  0 = eyes closed, 1 = fully open
//   blur      1 = vision not adjusted yet, 0 = sharp
//   glare     1 = over-exposed (just opened in a lit room), 0 = normal
public class CutsceneEyes : MonoBehaviour
{
    [Header("Current state (driven by the cutscene)")]
    [Range(0f, 1f)] public float openness = 1f;
    [Range(0f, 1f)] public float blur = 0f;
    [Range(0f, 1f)] public float glare = 0f;

    [Tooltip("Distance (m) the eyes focus on. Nearer and farther things get softer. 0 = no focus pull.")]
    public float focusDistance = 0f;

    [Tooltip("How strong the focus pull is (0-1).")]
    [Range(0f, 1f)] public float focusStrength = 0.6f;

    [Header("Eyelids (screen overlay)")]
    [Tooltip("Inside of the eyelids: almost black, slightly red (light shining through skin).")]
    public Color lidColor = new Color(0.035f, 0.004f, 0f, 1f);

    [Range(0.01f, 0.5f)] public float lidSoftness = 0.07f;

    [Tooltip("Extra lid edge softness while vision is blurry.")]
    [Range(0f, 0.6f)] public float blurryLidSoftness = 0.3f;

    public int overlaySortingOrder = 1000;

    [Tooltip("Width of the eyelid mask texture. Low is fine: it's soft anyway.")]
    public int maskResolution = 160;

    [Header("HDRP post-processing")]
    public int volumePriority = 100;
    [Tooltip("At full blur, everything closer than this (m) is out of focus.")]
    public float maxNearBlurDistance = 6f;
    public float closedExposure = -3.5f;
    public float glareExposure = 1.8f;
    public float closedSaturation = -60f;
    public float closedVignette = 0.55f;
    public float openVignette = 0.2f;
    public float maxChromaticAberration = 0.7f;
    public float glareBloom = 0.5f;

    [Header("RetroLook (your shader)")]
    [Tooltip("Volume that has the RetroLook override. Empty = found automatically.")]
    public Volume retroLookVolume;
    public float maxRetroBlur = 14f;
    public float extraRetroVignette = 0.35f;
    public float extraRetroChroma = 2f;

    // ---------------------------------------------------------------- runtime

    private bool initialized;
    private bool active;

    private Canvas overlayCanvas;
    private RawImage overlayImage;
    private Texture2D mask;
    private Color32[] maskPixels;
    private float lastOpen = -1f, lastSoft = -1f;

    private Volume volume;
    private VolumeProfile profile;
    private DepthOfField dof;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;
    private ChromaticAberration chromatic;
    private Bloom bloom;

    private RetroLook retro;
    private float retroBlur, retroVignette, retroChroma;
    private bool retroBlurOverride, retroVignetteOverride, retroChromaOverride;

    public bool IsActive { get { return active; } }

    // ---------------------------------------------------------------- public API

    // Turns the effect on (eyelids + volumes) with the given state.
    public void Activate(float open, float blurAmount, float glareAmount)
    {
        Init();

        openness = open;
        blur = blurAmount;
        glare = glareAmount;
        active = true;

        overlayCanvas.gameObject.SetActive(true);
        volume.weight = 1f;
        volume.enabled = true;

        Apply();
    }

    // Animates the eyelids (and optionally blur / glare) to a new state.
    // Pass -1 to leave blur or glare alone, so another animation can change them at the same time.
    public IEnumerator To(float open, float duration, float blurTarget = -1f, float glareTarget = -1f)
    {
        float o0 = openness, b0 = blur, g0 = glare;
        bool doBlur = blurTarget >= 0f;
        bool doGlare = glareTarget >= 0f;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            openness = Mathf.Lerp(o0, open, k);
            if (doBlur) blur = Mathf.Lerp(b0, blurTarget, k);
            if (doGlare) glare = Mathf.Lerp(g0, glareTarget, k);
            yield return null;
        }

        openness = open;
        if (doBlur) blur = blurTarget;
        if (doGlare) glare = glareTarget;
    }

    // Vision slowly settling: blur and glare fade to zero. Doesn't touch the eyelids,
    // so blinks can play at the same time.
    public IEnumerator AdjustVision(float duration)
    {
        float b0 = blur, g0 = glare;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            blur = Mathf.Lerp(b0, 0f, 1f - (1f - k) * (1f - k)); // fast at first, then settles
            glare = Mathf.Lerp(g0, 0f, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }

        blur = 0f;
        glare = 0f;
    }

    // A quick blink: close, stay shut for a moment, open again to where the eyes were.
    public IEnumerator Blink(float closeTime = 0.08f, float closedHold = 0.06f, float openTime = 0.18f)
    {
        float wasOpen = openness;
        yield return To(0f, closeTime);
        yield return new WaitForSeconds(closedHold);
        yield return To(wasOpen, openTime);
    }

    // Fades the whole effect out (eyes fully open, normal vision) and restores RetroLook.
    public IEnumerator FadeOut(float duration)
    {
        if (!active)
        {
            yield break;
        }

        yield return To(1f, duration, 0f, 0f);
        Release();
    }

    // Instantly back to normal vision.
    public void Release()
    {
        if (!initialized)
        {
            return;
        }

        active = false;
        openness = 1f;
        blur = 0f;
        glare = 0f;
        focusDistance = 0f;

        overlayCanvas.gameObject.SetActive(false);
        volume.weight = 0f;
        volume.enabled = false;

        RestoreRetroLook();
    }

    // ---------------------------------------------------------------- setup

    private void Init()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        // Overlay canvas with one stretched RawImage showing the eyelid mask.
        GameObject canvasGO = new GameObject("Eyes_Overlay");
        canvasGO.transform.SetParent(transform, false);
        overlayCanvas = canvasGO.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = overlaySortingOrder;

        GameObject imageGO = new GameObject("Eyelids");
        imageGO.transform.SetParent(canvasGO.transform, false);
        overlayImage = imageGO.AddComponent<RawImage>();
        overlayImage.raycastTarget = false;

        RectTransform rt = overlayImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        int w = Mathf.Max(32, maskResolution);
        int h = Mathf.Max(18, w * 9 / 16);
        mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
        mask.wrapMode = TextureWrapMode.Clamp;
        mask.filterMode = FilterMode.Bilinear;
        maskPixels = new Color32[w * h];
        overlayImage.texture = mask;

        // Own global Volume with a runtime profile (no assets needed).
        GameObject volumeGO = new GameObject("Eyes_Volume");
        volumeGO.transform.SetParent(transform, false);
        volume = volumeGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = volumePriority;
        volume.weight = 0f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Eyes_Profile (runtime)";
        dof = profile.Add<DepthOfField>(false);
        vignette = profile.Add<Vignette>(false);
        colorAdjustments = profile.Add<ColorAdjustments>(false);
        chromatic = profile.Add<ChromaticAberration>(false);
        bloom = profile.Add<Bloom>(false);
        volume.sharedProfile = profile;

        FindRetroLook();
    }

    private void FindRetroLook()
    {
        if (retroLookVolume == null)
        {
            foreach (Volume v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v != volume && v.sharedProfile != null && v.sharedProfile.Has<RetroLook>())
                {
                    retroLookVolume = v;
                    break;
                }
            }
        }

        // .profile makes a runtime copy, so the profile asset isn't changed in play mode.
        if (retroLookVolume != null && retroLookVolume.profile.TryGet(out retro))
        {
            retroBlur = retro.blur.value;
            retroBlurOverride = retro.blur.overrideState;
            retroVignette = retro.vignette.value;
            retroVignetteOverride = retro.vignette.overrideState;
            retroChroma = retro.chromaticAberration.value;
            retroChromaOverride = retro.chromaticAberration.overrideState;
        }
    }

    private void RestoreRetroLook()
    {
        if (retro == null)
        {
            return;
        }

        retro.blur.value = retroBlur;
        retro.blur.overrideState = retroBlurOverride;
        retro.vignette.value = retroVignette;
        retro.vignette.overrideState = retroVignetteOverride;
        retro.chromaticAberration.value = retroChroma;
        retro.chromaticAberration.overrideState = retroChromaOverride;
    }

    // ---------------------------------------------------------------- per frame

    private void LateUpdate()
    {
        if (active)
        {
            Apply();
        }
    }

    private void Apply()
    {
        float open = Mathf.Clamp01(openness);
        float b = Mathf.Clamp01(blur);
        float g = Mathf.Clamp01(glare);

        UpdateMask(open, lidSoftness + b * blurryLidSoftness);

        // Depth of field: blurry vision blurs near and far; a focus distance keeps one depth sharp.
        float nearEnd = b * maxNearBlurDistance;
        float farStart = Mathf.Lerp(80f, 0.25f, b);
        float farRange = Mathf.Lerp(40f, 1.5f, b);

        if (focusDistance > 0.01f)
        {
            float s = focusStrength * (1f - b);
            nearEnd = Mathf.Max(nearEnd, focusDistance * 0.55f * s);
            farStart = Mathf.Min(farStart, Mathf.Lerp(80f, focusDistance * 1.6f, s));
        }

        bool useDof = nearEnd > 0.01f || farStart < 79f;
        Set(dof.focusMode, useDof ? DepthOfFieldMode.Manual : DepthOfFieldMode.Off);
        Set(dof.nearFocusStart, 0f);
        Set(dof.nearFocusEnd, Mathf.Max(0.0001f, nearEnd));
        Set(dof.farFocusStart, farStart);
        Set(dof.farFocusEnd, farStart + farRange);

        Set(vignette.intensity, Mathf.Clamp01(Mathf.Lerp(closedVignette, openVignette, open) + b * 0.15f));
        Set(vignette.smoothness, 0.6f);

        Set(colorAdjustments.postExposure, Mathf.Lerp(closedExposure, 0f, open) + g * glareExposure);
        Set(colorAdjustments.saturation, Mathf.Clamp(Mathf.Lerp(closedSaturation, 0f, open) - b * 25f, -100f, 100f));

        Set(chromatic.intensity, Mathf.Clamp01(b * maxChromaticAberration));
        Set(bloom.intensity, Mathf.Clamp01(g * glareBloom));

        if (retro != null)
        {
            retro.blur.overrideState = true;
            retro.blur.value = Mathf.Clamp(retroBlur + b * maxRetroBlur, 0f, 32f);
            retro.vignette.overrideState = true;
            retro.vignette.value = Mathf.Clamp01(retroVignette + (1f - open) * extraRetroVignette);
            retro.chromaticAberration.overrideState = true;
            retro.chromaticAberration.value = Mathf.Clamp(retroChroma + b * extraRetroChroma, 0f, 4f);
        }
    }

    private static void Set<T>(VolumeParameter<T> parameter, T value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    // Eye-shaped opening: an ellipse that grows from a thin slit to wider than the screen.
    // The upper lid hangs a bit lower while the eyes are nearly shut.
    private void UpdateMask(float open, float softness)
    {
        if (Mathf.Abs(open - lastOpen) < 0.002f && Mathf.Abs(softness - lastSoft) < 0.002f)
        {
            return;
        }

        lastOpen = open;
        lastSoft = softness;

        if (open >= 0.999f && softness <= lidSoftness + 0.01f)
        {
            overlayImage.enabled = false;
            return;
        }

        overlayImage.enabled = true;

        int w = mask.width;
        int h = mask.height;
        float a = 1.15f + 1.1f * open;
        float bAxis = Mathf.Max(0.0005f, 1.65f * open);
        float centreY = -0.12f * (1f - open);
        Color32 lid = lidColor;

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h * 2f - 1f;
            float dy = (v - centreY) / bAxis;

            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float du = u / a;
                float d = Mathf.Sqrt(du * du + dy * dy) - 1f; // < 0 inside the opening

                float alpha = open <= 0.001f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-softness, softness, d));
                maskPixels[y * w + x] = new Color32(lid.r, lid.g, lid.b, (byte)(alpha * 255f));
            }
        }

        mask.SetPixels32(maskPixels);
        mask.Apply(false);
    }

    private void OnDestroy()
    {
        RestoreRetroLook();

        if (mask != null) Destroy(mask);
        if (profile != null) Destroy(profile);
    }
}
