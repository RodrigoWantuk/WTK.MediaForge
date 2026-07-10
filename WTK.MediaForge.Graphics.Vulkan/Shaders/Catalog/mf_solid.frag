#version 450

layout(push_constant) uniform SolidParams
{
    vec4 fillColor;
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

void main()
{
    vec2 layerUv = mapFragmentToLayerUv(vUv);
    if (layerUv.x < params.cropRect.x || layerUv.x > params.cropRect.z ||
        layerUv.y < params.cropRect.y || layerUv.y > params.cropRect.w)
    {
        outColor = vec4(0.0);
        return;
    }

    outColor = vec4(params.fillColor.rgb, params.fillColor.a * params.opacity);
}
