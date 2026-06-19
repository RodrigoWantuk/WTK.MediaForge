#version 450

layout(set = 0, binding = 0) uniform sampler2D uCanvasTexture;

layout(push_constant) uniform OutputParams
{
    vec2 canvasSize;
    vec2 outputSize;
    vec4 letterboxColor;
    int layoutMode;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

vec2 computeFitUv(vec2 uv, vec2 srcSize, vec2 dstSize)
{
    float srcAspect = srcSize.x / max(srcSize.y, 0.0001);
    float dstAspect = dstSize.x / max(dstSize.y, 0.0001);
    vec2 scale;

    if (srcAspect > dstAspect)
        scale = vec2(1.0, dstAspect / srcAspect);
    else
        scale = vec2(srcAspect / dstAspect, 1.0);

    return (uv - vec2(0.5)) / scale + vec2(0.5);
}

void main()
{
    vec2 uv = computeFitUv(vUv, params.canvasSize, params.outputSize);

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
    {
        outColor = params.letterboxColor;
        return;
    }

    outColor = texture(uCanvasTexture, uv);
}
