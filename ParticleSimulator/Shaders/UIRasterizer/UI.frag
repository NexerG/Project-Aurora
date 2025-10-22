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
    vec3 msdf = texture(samplers[fragInstanceID], fragUV).rgb;
    float sd = median(msdf.r, msdf.g, msdf.b) - 0.5f;
    float screenPxRange = fwidth(sd);
    float opacity = clamp(sd / screenPxRange + 0.5f, 0.0f, 1.0f);
    //float opacity = smoothstep(-screenPxRange, screenPxRange, sd);

    vec3 color = fragStyle.tint;
    outColor = vec4(color, opacity);
}