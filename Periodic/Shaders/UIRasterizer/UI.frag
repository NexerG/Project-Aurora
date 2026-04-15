#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

struct Style
{
    vec3 tint;
};

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragInstanceID;
layout(location = 2) in Style fragStyle;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D samplers[];

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main()
{
    vec4 mtsdf = texture(samplers[fragInstanceID], fragUV);
    float msdfDist = median(mtsdf.r, mtsdf.g, mtsdf.b);
    float trueDist = mtsdf.a;

    float sd = msdfDist;
    float trueSD = trueDist;
    if(abs((sd - 0.5f) - (trueSD - 0.5f)) > 0.1f)
    {
        sd = trueSD;
    }

    float pxRange = 4.0;
    vec2 atlasSize = vec2(textureSize(samplers[fragInstanceID], 0));
    vec2 unitRange = vec2(pxRange) / atlasSize;
    vec2 screenTexSize = vec2(1.0) / fwidth(fragUV);
    float screenPxRange = max(1.0, length(unitRange * screenTexSize));

    float screenPxDist = screenPxRange * (sd - 0.5);
    float opacity = clamp(screenPxDist + 0.5, 0.0, 1.0);

    vec3 color = fragStyle.tint;
    outColor = vec4(color, opacity);
}