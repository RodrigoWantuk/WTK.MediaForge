#version 450

layout(set = 0, binding = 0) uniform sampler2D uOriginalTexture;
layout(set = 0, binding = 1) uniform sampler2D uEffectTexture;

layout(push_constant) uniform MaskCompositeParams
{
    vec4 bounds;
    vec4 parameters;
    int shapeKind;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

float featheredCoverage(float distanceInside)
{
    float feather = max(params.parameters.x, 0.0);
    return feather <= 0.00001
        ? step(0.0, distanceInside)
        : smoothstep(0.0, feather, distanceInside);
}

float rectangularCoverage(vec2 localPoint)
{
    float distanceInside = min(
        min(localPoint.x, 1.0 - localPoint.x),
        min(localPoint.y, 1.0 - localPoint.y));
    return featheredCoverage(distanceInside);
}

float roundedRectangleCoverage(vec2 localPoint)
{
    float radius = clamp(params.parameters.w, 0.0, 0.5);
    vec2 point = abs(localPoint - vec2(0.5)) - vec2(0.5 - radius);
    float signedDistance = length(max(point, vec2(0.0))) - radius;
    return featheredCoverage(-signedDistance);
}

float ellipseCoverage(vec2 localPoint)
{
    float distanceInside = 1.0 - length((localPoint - vec2(0.5)) * 2.0);
    return featheredCoverage(distanceInside);
}

float maskCoverage()
{
    vec2 extent = max(params.bounds.zw - params.bounds.xy, vec2(0.00001));
    vec2 localPoint = (vUv - params.bounds.xy) / extent;

    if (params.shapeKind == 0)
        return rectangularCoverage(localPoint);
    if (params.shapeKind == 1)
        return roundedRectangleCoverage(localPoint);
    return ellipseCoverage(localPoint);
}

void main()
{
    vec4 original = texture(uOriginalTexture, vUv);
    vec4 effectResult = texture(uEffectTexture, vUv);
    float coverage = maskCoverage();
    if (params.parameters.z > 0.5)
        coverage = 1.0 - coverage;

    coverage = clamp(coverage * params.parameters.y, 0.0, 1.0);
    outColor = mix(original, effectResult, coverage);
}
