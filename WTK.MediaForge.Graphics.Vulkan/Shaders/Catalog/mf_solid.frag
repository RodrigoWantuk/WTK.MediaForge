#version 450

layout(push_constant) uniform SolidParams
{
    vec4 fillColor;
    float opacity;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    outColor = vec4(params.fillColor.rgb, params.fillColor.a * params.opacity);
}
