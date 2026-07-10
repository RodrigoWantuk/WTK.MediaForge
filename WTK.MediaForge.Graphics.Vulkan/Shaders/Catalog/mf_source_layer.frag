#version 450

layout(set = 0, binding = 0) uniform sampler2D uSourceTexture;

layout(push_constant) uniform LayerParams
{
    vec4 cropRect;
    vec4 chromaKeyColor;
    vec4 chromaKeyParameters;
    vec2 logicalSize;
    vec2 boxSize;
    vec2 pivot;
    float opacity;
    int layoutMode;
    int contentRotation;
    float rotationDegrees;
    vec4 letterboxColor;
    vec4 geometryRect;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

vec2 computeLayoutUv(vec2 localUv, int layoutMode, vec2 contentSize, vec2 boxSize)
{
    // Skeleton: Fit / Fill / Stretch mapping from box UV to content UV.
    float contentAspect = contentSize.x / max(contentSize.y, 0.0001);
    float boxAspect = boxSize.x / max(boxSize.y, 0.0001);
    vec2 scale = vec2(1.0);

    if (layoutMode == 0)
    {
        if (contentAspect > boxAspect)
            scale = vec2(1.0, boxAspect / contentAspect);
        else
            scale = vec2(contentAspect / boxAspect, 1.0);
    }
    else if (layoutMode == 1)
    {
        if (contentAspect > boxAspect)
            scale = vec2(contentAspect / boxAspect, 1.0);
        else
            scale = vec2(1.0, boxAspect / contentAspect);
    }

    return (localUv - vec2(0.5)) / scale + vec2(0.5);
}

vec2 mapCroppedUvToFullLogicalUv(vec2 uvInCropped, vec4 crop)
{
    return vec2(
        mix(crop.x, crop.z, uvInCropped.x),
        mix(crop.y, crop.w, uvInCropped.y));
}

vec2 rotateUv(vec2 uv, int rotation)
{
    if (rotation == 0)
        return uv;

    vec2 centered = uv - vec2(0.5);
    if (rotation == 1)
        centered = vec2(centered.y, -centered.x);
    else if (rotation == 2)
        centered = -centered;
    else if (rotation == 3)
        centered = vec2(-centered.y, centered.x);
    return centered + vec2(0.5);
}

vec4 applyChromaKey(vec4 color)
{
    if (params.chromaKeyParameters.w < 0.5)
        return color;

    vec3 key = params.chromaKeyColor.rgb;
    float similarity = params.chromaKeyParameters.x;
    float smoothness = max(params.chromaKeyParameters.y, 0.0001);
    float spillReduction = params.chromaKeyParameters.z;

    float distanceToKey = distance(color.rgb, key);
    float matte = smoothstep(similarity, similarity + smoothness, distanceToKey);
    float alpha = color.a * matte;

    vec3 spillAxis = normalize(max(key, vec3(0.0001)));
    float spillAmount = max(dot(color.rgb - key, spillAxis), 0.0);
    vec3 despilled = max(color.rgb - spillAxis * spillAmount * spillReduction * (1.0 - matte), vec3(0.0));

    return vec4(despilled, alpha);
}

vec2 mapFragmentToLayerUv(vec2 fragmentUv)
{
    vec2 localPoint = params.geometryRect.xy + fragmentUv * params.geometryRect.zw;

    if (abs(params.rotationDegrees) < 0.001)
        return localPoint / max(params.boxSize, vec2(0.0001));

    float radians = -params.rotationDegrees * 0.017453292519943295;
    float c = cos(radians);
    float s = sin(radians);
    vec2 pivotPx = params.pivot * params.boxSize;
    vec2 centered = localPoint - pivotPx;
    vec2 rotated = vec2(centered.x * c - centered.y * s, centered.x * s + centered.y * c);
    return (rotated + pivotPx) / max(params.boxSize, vec2(0.0001));
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

    vec2 croppedSize = params.logicalSize * vec2(
        params.cropRect.z - params.cropRect.x,
        params.cropRect.w - params.cropRect.y);

    vec2 uvInCropped = computeLayoutUv(layerUv, params.layoutMode, croppedSize, params.boxSize);
    if (uvInCropped.x < 0.0 || uvInCropped.x > 1.0 ||
        uvInCropped.y < 0.0 || uvInCropped.y > 1.0)
    {
        outColor = vec4(params.letterboxColor.rgb, params.letterboxColor.a * params.opacity);
        return;
    }

    vec2 uvLogical = mapCroppedUvToFullLogicalUv(uvInCropped, params.cropRect);
    vec2 uvRaw = rotateUv(uvLogical, params.contentRotation);

    vec4 color = texture(uSourceTexture, uvRaw);
    color = applyChromaKey(color);
    outColor = vec4(color.rgb, color.a * params.opacity);
}
