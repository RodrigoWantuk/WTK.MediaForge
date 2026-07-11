#version 450

layout(set = 0, binding = 0) uniform sampler2D uInputTexture;

layout(push_constant) uniform BlurParams
{
    vec2 texelSize;
    vec2 direction;
    float radius;
} params;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    int radiusPx = int(clamp(ceil(params.radius), 1.0, 32.0));
    vec4 sum = vec4(0.0);
    float weightSum = 0.0;

    for (int i = -32; i <= 32; i++)
    {
        if (i < -radiusPx || i > radiusPx)
            continue;

        float distanceFromCenter = abs(float(i)) / max(float(radiusPx), 1.0);
        float weight = 1.0 - distanceFromCenter * 0.5;
        vec2 uv = clamp(vUv + params.direction * params.texelSize * float(i), vec2(0.0), vec2(1.0));
        sum += texture(uInputTexture, uv) * weight;
        weightSum += weight;
    }

    outColor = sum / max(weightSum, 0.0001);
}
