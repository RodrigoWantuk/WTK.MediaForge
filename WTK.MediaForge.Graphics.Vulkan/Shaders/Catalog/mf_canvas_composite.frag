#version 450

layout(set = 0, binding = 0) uniform sampler2D uCanvasTexture;

layout(push_constant) uniform CanvasParams
{
    float opacity;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 color = texture(uCanvasTexture, vUv);
    outColor = vec4(color.rgb, color.a * params.opacity);
}
