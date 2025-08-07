#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

struct Style
{
    vec3 tintDefault;
    vec3 tintHover;
    vec3 tintClick;
};

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragInstanceID;
layout(location = 2) in Style fragStyle;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 2) uniform sampler2D textures;

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main()
{
    vec3 msdf = texture(textures, fragUV).rgb;
    float sd = median(msdf.r, msdf.g, msdf.b) - 0.5f;
    float screenPxRange = fwidth(sd);
    float opacity = clamp(sd / screenPxRange + 0.5f, 0.0f, 1.0f);
    //float opacity = smoothstep(-screenPxRange, screenPxRange, sd);

    vec3 color = fragStyle.tintDefault;
    outColor = vec4(color, opacity);
}