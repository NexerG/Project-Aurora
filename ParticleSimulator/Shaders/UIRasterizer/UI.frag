#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragInstanceID;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 2) uniform sampler2D textures[];

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main()
{
    vec3 msdf = texture(textures[fragInstanceID], fragUV).rgb;
    float sd = median(msdf.r, msdf.g, msdf.b) - 0.43;
    float screenPxRange = fwidth(sd);
    float opacity = smoothstep(-screenPxRange, screenPxRange, sd);

    //outColor = vec4(msdf, 1.0f);
    //outColor = vec4(vec3(sd), 1);
    //outColor = vec4(vec3(screenPxRange), 1);
    //outColor = vec4(vec3(opacity), 1);
    outColor = vec4(vec3(1.0f), opacity);
}