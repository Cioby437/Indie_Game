Shader "Hidden/Shader/RetroLook"
{
    Properties
    {
        _MainTex("Main Texture", 2DArray) = "grey" {}
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord   = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    TEXTURE2D_X(_MainTex);

    float  _PixelSize;
    float  _ColorSteps;
    float  _Dither;
    float4 _Tint;
    float  _Saturation;
    float  _Contrast;
    float  _ShadowLift;
    float  _Grain;
    float  _GrainSpeed;
    float  _Vignette;
    float  _VignetteSoft;
    float  _Chromatic;
    float  _Scanlines;

    // 4x4 Bayer matrix for ordered dithering
    static const float kBayer4[16] =
    {
         0,  8,  2, 10,
        12,  4, 14,  6,
         3, 11,  1,  9,
        15,  7, 13,  5
    };

    float Bayer4(uint2 p)
    {
        return (kBayer4[(p.x & 3) + (p.y & 3) * 4] + 0.5) / 16.0 - 0.5; // -0.5..0.5
    }

    float Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float3 LoadSource(float2 pixel)
    {
        uint2 c = (uint2)clamp(pixel, 0.0, _ScreenSize.xy - 1.0);
        return LOAD_TEXTURE2D_X(_MainTex, c).rgb;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 screen = _ScreenSize.xy;
        float  px     = max(_PixelSize, 1.0);

        // --- Pixelation: snap every pixel to the centre of its block ---
        float2 blockId = floor(input.positionCS.xy / px);
        float2 center  = min(blockId * px + px * 0.5, screen - 1.0);
        float2 uv      = center / screen;

        // --- Chromatic aberration (stays on the pixel grid) ---
        float2 fromCenter = uv - 0.5;
        float2 caOffset   = round(fromCenter * 2.0 * _Chromatic) * px;
        float3 col;
        col.r = LoadSource(center + caOffset).r;
        col.g = LoadSource(center).g;
        col.b = LoadSource(center - caOffset).b;

        // Work in gamma space so banding/dither looks like old hardware
        col = LinearToSRGB(saturate(col));

        // --- Grade ---
        float lum = dot(col, float3(0.299, 0.587, 0.114));
        col = lerp(lum.xxx, col, _Saturation);
        col = (col - 0.5) * _Contrast + 0.5;
        col += _ShadowLift;
        col *= _Tint.rgb;

        // --- Film grain (per big pixel, stepped in time) ---
        float t = floor(_Time.y * _GrainSpeed);
        float n = Hash21(blockId + t * 17.13) - 0.5;
        col += n * _Grain;

        // --- Vignette (aspect-corrected, pixelated) ---
        float2 v = fromCenter;
        v.x *= screen.x / screen.y;
        float d      = length(v);
        float radius = lerp(1.1, 0.35, _Vignette);
        float vig    = smoothstep(radius, radius - _VignetteSoft, d);
        col *= lerp(1.0, vig, saturate(_Vignette * 1.5));

        // --- Scanlines (optional) ---
        col *= 1.0 - _Scanlines * 0.5 * fmod(blockId.y, 2.0);

        // --- Posterize with ordered dither ---
        float steps = max(_ColorSteps - 1.0, 1.0);
        float dither = Bayer4((uint2)blockId) * _Dither;
        col = floor(saturate(col) * steps + 0.5 + dither) / steps;

        col = SRGBToLinear(saturate(col));
        return float4(col, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "RetroLook"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
