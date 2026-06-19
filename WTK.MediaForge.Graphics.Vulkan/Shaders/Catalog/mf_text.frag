#version 450

layout(set = 0, binding = 0) uniform sampler2D uTextTexture;

layout(push_constant) uniform TextParams
{
    vec4 textColor;
    float opacity;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 sampled = texture(uTextTexture, vUv);
    vec4 color = vec4(params.textColor.rgb, sampled.a * params.textColor.a);
    outColor = vec4(color.rgb, color.a * params.opacity);
}
