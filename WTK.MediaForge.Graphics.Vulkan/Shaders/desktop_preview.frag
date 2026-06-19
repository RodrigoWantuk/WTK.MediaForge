#version 450

layout(set = 0, binding = 0) uniform sampler2D uDesktopTexture;
layout(set = 0, binding = 1) uniform sampler2D uOverlayTexture;

layout(push_constant) uniform PreviewParams
{
    vec2 sourceSize;
    vec2 viewportSize;
    int rotation;
    int hasOverlay;
    vec2 overlaySize;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

vec2 rotateUv(vec2 uv, int rot)
{
    if (rot == 0)
        return uv;

    vec2 centered = uv - vec2(0.5);

    if (rot == 1)
        centered = vec2(centered.y, -centered.x);
    else if (rot == 2)
        centered = -centered;
    else if (rot == 3)
        centered = vec2(-centered.y, centered.x);

    return centered + vec2(0.5);
}

vec2 computeFitUv(vec2 uv, vec2 srcSize, vec2 vpSize)
{
    float srcAspect = srcSize.x / srcSize.y;
    float vpAspect = vpSize.x / vpSize.y;
    vec2 scale;

    if (srcAspect > vpAspect)
        scale = vec2(1.0, vpAspect / srcAspect);
    else
        scale = vec2(srcAspect / vpAspect, 1.0);

    return (uv - 0.5) / scale + 0.5;
}

void main()
{
    vec4 backgroundColor = vec4(0.06, 0.08, 0.13, 1.0);

    vec2 uv = computeFitUv(vUv, params.sourceSize, params.viewportSize);
    uv = rotateUv(uv, params.rotation);

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
    {
        outColor = backgroundColor;
        return;
    }

    vec4 color = texture(uDesktopTexture, uv);

    if (params.hasOverlay != 0)
    {
        float pad = 16.0 / params.viewportSize.y;
        vec2 overlayNorm = params.overlaySize / params.viewportSize;
        vec2 overlayPos = vec2(pad, 1.0 - overlayNorm.y - pad);
        vec2 overlayUv = (vUv - overlayPos) / overlayNorm;

        if (overlayUv.x >= 0.0 && overlayUv.x <= 1.0 &&
            overlayUv.y >= 0.0 && overlayUv.y <= 1.0)
        {
            vec4 overlay = texture(uOverlayTexture, overlayUv);
            color = mix(color, overlay, overlay.a);
        }
    }

    outColor = color;
}
