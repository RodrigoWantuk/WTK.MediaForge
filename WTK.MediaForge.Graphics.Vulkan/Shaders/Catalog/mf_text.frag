#version 450

layout(set = 0, binding = 0) uniform sampler2D uTextTexture;

layout(push_constant) uniform TextParams
{
    vec4 textColor;
    vec4 cropRect;
    vec4 geometryRect;
    vec2 boxSize;
    vec2 pivot;
    float opacity;
    float rotationDegrees;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

vec2 mapFragmentToLayerUv(vec2 fragmentUv)
{
    vec2 localPoint = params.geometryRect.xy + fragmentUv * params.geometryRect.zw;

    if (abs(params.rotationDegrees) >= 0.001)
    {
        float radians = -params.rotationDegrees * 0.017453292519943295;
        float c = cos(radians);
        float s = sin(radians);
        vec2 pivotPx = params.pivot * params.boxSize;
        vec2 centered = localPoint - pivotPx;
        localPoint = vec2(centered.x * c - centered.y * s, centered.x * s + centered.y * c) + pivotPx;
    }

    return localPoint / max(params.boxSize, vec2(0.0001));
}

vec2 mapCrop(vec2 uv)
{
    return vec2(
        mix(params.cropRect.x, params.cropRect.z, uv.x),
        mix(params.cropRect.y, params.cropRect.w, uv.y));
}

void main()
{
    vec2 layerUv = mapFragmentToLayerUv(vUv);
    if (layerUv.x < 0.0 || layerUv.x > 1.0 ||
        layerUv.y < 0.0 || layerUv.y > 1.0)
    {
        outColor = vec4(0.0);
        return;
    }

    vec4 sampled = texture(uTextTexture, mapCrop(layerUv));
    vec4 color = vec4(params.textColor.rgb, sampled.a * params.textColor.a);
    outColor = vec4(color.rgb, color.a * params.opacity);
}
