using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Inscryption-style post-process for HDRP (Unity 6):
// chunky pixels, ordered dithering, limited colour depth, warm grade, grain, vignette.
[Serializable, VolumeComponentMenu("Post-processing/Custom/Retro Look")]
public sealed class RetroLook : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    [Header("General")]
    public BoolParameter enable = new BoolParameter(false);

    [Header("Pixelation")]
    [Tooltip("Size of one 'big pixel' in screen pixels. 3-5 looks close to Inscryption at 1080p.")]
    public ClampedIntParameter pixelSize = new ClampedIntParameter(4, 1, 16);

    [Header("Blur")]
    [Tooltip("Blur radius in screen pixels. 0 = sharp. Driven by EyelidBlink while the eyes open.")]
    public ClampedFloatParameter blur = new ClampedFloatParameter(0f, 0f, 32f);

    [Header("Colour Depth & Dither")]
    [Tooltip("Colour levels per channel. Lower = more banding/dither.")]
    public ClampedIntParameter colorSteps = new ClampedIntParameter(16, 2, 64);
    [Tooltip("Strength of the 4x4 Bayer ordered dither.")]
    public ClampedFloatParameter ditherStrength = new ClampedFloatParameter(1f, 0f, 2f);

    [Header("Grade")]
    public ColorParameter tint = new ColorParameter(new Color(1f, 0.88f, 0.72f), false, false, true);
    public ClampedFloatParameter saturation = new ClampedFloatParameter(0.8f, 0f, 2f);
    public ClampedFloatParameter contrast = new ClampedFloatParameter(1.2f, 0.5f, 2f);
    [Tooltip("Lifts/crushes shadows. Negative = darker blacks.")]
    public ClampedFloatParameter shadowLift = new ClampedFloatParameter(-0.02f, -0.2f, 0.2f);

    [Header("Film")]
    public ClampedFloatParameter grain = new ClampedFloatParameter(0.06f, 0f, 0.5f);
    [Tooltip("How many times per second the grain pattern changes.")]
    public ClampedFloatParameter grainSpeed = new ClampedFloatParameter(12f, 0f, 60f);
    public ClampedFloatParameter vignette = new ClampedFloatParameter(0.55f, 0f, 1f);
    public ClampedFloatParameter vignetteSoftness = new ClampedFloatParameter(0.5f, 0.01f, 1f);
    [Tooltip("Colour fringing toward the screen edges, in big pixels.")]
    public ClampedFloatParameter chromaticAberration = new ClampedFloatParameter(0.6f, 0f, 4f);
    [Tooltip("Optional dark line on every other big-pixel row.")]
    public ClampedFloatParameter scanlines = new ClampedFloatParameter(0f, 0f, 1f);

    const string kShaderName = "Hidden/Shader/RetroLook";
    Material m_Material;

    static readonly int _MainTex        = Shader.PropertyToID("_MainTex");
    static readonly int _PixelSize      = Shader.PropertyToID("_PixelSize");
    static readonly int _Blur           = Shader.PropertyToID("_Blur");
    static readonly int _ColorSteps     = Shader.PropertyToID("_ColorSteps");
    static readonly int _Dither         = Shader.PropertyToID("_Dither");
    static readonly int _Tint           = Shader.PropertyToID("_Tint");
    static readonly int _Saturation     = Shader.PropertyToID("_Saturation");
    static readonly int _Contrast       = Shader.PropertyToID("_Contrast");
    static readonly int _ShadowLift     = Shader.PropertyToID("_ShadowLift");
    static readonly int _Grain          = Shader.PropertyToID("_Grain");
    static readonly int _GrainSpeed     = Shader.PropertyToID("_GrainSpeed");
    static readonly int _Vignette       = Shader.PropertyToID("_Vignette");
    static readonly int _VignetteSoft   = Shader.PropertyToID("_VignetteSoft");
    static readonly int _Chromatic      = Shader.PropertyToID("_Chromatic");
    static readonly int _Scanlines      = Shader.PropertyToID("_Scanlines");

    public bool IsActive() => m_Material != null && enable.value;

    public override CustomPostProcessInjectionPoint injectionPoint =>
        CustomPostProcessInjectionPoint.AfterPostProcess;

    public override void Setup()
    {
        var shader = Shader.Find(kShaderName);
        if (shader != null)
            m_Material = new Material(shader);
        else
            Debug.LogError($"RetroLook: shader '{kShaderName}' not found. " +
                           "Add it to Project Settings > Graphics > Always Included Shaders.");
    }

    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
    {
        if (m_Material == null) return;

        m_Material.SetTexture(_MainTex, source);
        m_Material.SetFloat(_PixelSize, pixelSize.value);
        m_Material.SetFloat(_Blur, blur.value);
        m_Material.SetFloat(_ColorSteps, colorSteps.value);
        m_Material.SetFloat(_Dither, ditherStrength.value);
        m_Material.SetColor(_Tint, tint.value);
        m_Material.SetFloat(_Saturation, saturation.value);
        m_Material.SetFloat(_Contrast, contrast.value);
        m_Material.SetFloat(_ShadowLift, shadowLift.value);
        m_Material.SetFloat(_Grain, grain.value);
        m_Material.SetFloat(_GrainSpeed, grainSpeed.value);
        m_Material.SetFloat(_Vignette, vignette.value);
        m_Material.SetFloat(_VignetteSoft, vignetteSoftness.value);
        m_Material.SetFloat(_Chromatic, chromaticAberration.value);
        m_Material.SetFloat(_Scanlines, scanlines.value);

        HDUtils.DrawFullScreen(cmd, m_Material, destination, shaderPassId: 0);
    }

    public override void Cleanup()
    {
        CoreUtils.Destroy(m_Material);
    }
}
