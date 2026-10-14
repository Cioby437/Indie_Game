Shader "FullScreen/MacintoshFilter"
{
    Properties
    {
        _PixelSize ("Pixel Size", Float) = 6.0
        _ColorLevels ("Color Levels", Float) = 4.0
        _DitherStrength ("Dither Strength", Range(0, 1)) = 0.5
        _EffectStrength ("Effect Strength", Range(0, 1)) = 1.0

        _RedFilter ("Red Filter", Range(0, 2)) = 1.0
        _GreenFilter ("Green Filter", Range(0, 2)) = 1.0
        _BlueFilter ("Blue Filter", Range(0, 2)) = 1.0

        _BWFilter ("Black & White Filter", Range(0, 1)) = 0.0
    }

    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5

    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    float _PixelSize;
    float _ColorLevels;
    float _DitherStrength;
    float _EffectStrength;

    float _RedFilter;
    float _GreenFilter;
    float _BlueFilter;

    float _BWFilter;


    float GetDither(float2 pixel)
    {
        int x = (int)fmod(pixel.x, 4.0);
        int y = (int)fmod(pixel.y, 4.0);

        if (x == 0 && y == 0) return 0.0;
        if (x == 1 && y == 0) return 0.5;
        if (x == 2 && y == 0) return 0.125;
        if (x == 3 && y == 0) return 0.625;

        if (x == 0 && y == 1) return 0.75;
        if (x == 1 && y == 1) return 0.25;
        if (x == 2 && y == 1) return 0.875;
        if (x == 3 && y == 1) return 0.375;

        if (x == 0 && y == 2) return 0.1875;
        if (x == 1 && y == 2) return 0.6875;
        if (x == 2 && y == 2) return 0.0625;
        if (x == 3 && y == 2) return 0.5625;

        if (x == 0 && y == 3) return 0.9375;
        if (x == 1 && y == 3) return 0.4375;
        if (x == 2 && y == 3) return 0.8125;
        if (x == 3 && y == 3) return 0.3125;

        return 0.0;
    }


    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float2 screenPixel = varyings.positionCS.xy;

        // Original camera color
        float3 originalColor =
            CustomPassLoadCameraColor(screenPixel, 0);

        // Pixelation
        float pixelSize = max(_PixelSize, 1.0);

        float2 pixelatedPosition =
            floor(screenPixel / pixelSize) * pixelSize;

        float3 color =
            CustomPassLoadCameraColor(pixelatedPosition, 0);


        // --------------------------------
        // RGB FILTERS
        // --------------------------------

        color.r *= _RedFilter;
        color.g *= _GreenFilter;
        color.b *= _BlueFilter;

        color = saturate(color);

        // Black & White Filter

        float bwBrightness =
            color.r * 0.299 +
            color.g * 0.587 +
            color.b * 0.114;

        float3 bwColor =
            float3(
                bwBrightness,
                bwBrightness,
                bwBrightness
            );

        color =
            lerp(
                color,
                bwColor,
                _BWFilter
            );


        // --------------------------------
        // DITHERING
        // --------------------------------

        float2 ditherPosition =
            floor(screenPixel / pixelSize);

        float dither =
            GetDither(ditherPosition);


        // Calculate brightness only for
        // controlling the dithering levels.
        float brightness =
            color.r * 0.299 +
            color.g * 0.587 +
            color.b * 0.114;


        brightness +=
            (dither - 0.5) * _DitherStrength;


        float levels =
            max(_ColorLevels, 1.0);

        brightness =
            floor(brightness * levels) / levels;

        brightness =
            saturate(brightness);


        // Apply the brightness change
        // to the original filtered color.
        float3 processedColor =
            color * brightness;


        // --------------------------------
        // FINAL EFFECT STRENGTH
        // --------------------------------

        float3 finalColor =
            lerp(
                originalColor,
                processedColor,
                _EffectStrength
            );


        return float4(
            finalColor.r,
            finalColor.g,
            finalColor.b,
            1.0
        );
    }

    ENDHLSL


    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
        }

        Pass
        {
            Name "Macintosh Filter"

            ZWrite Off
            ZTest Always

            Blend SrcAlpha OneMinusSrcAlpha

            Cull Off

            HLSLPROGRAM
            #pragma fragment FullScreenPass
            ENDHLSL
        }
    }

    Fallback Off
}