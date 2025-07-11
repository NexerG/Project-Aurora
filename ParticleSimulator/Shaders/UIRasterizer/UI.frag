#version 450
#extension GL_EXT_nonuniform_qualifier : enable

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragInstanceID;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 2) uniform sampler2D textures[];

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main() {
    vec3 msdf = texture(textures[fragInstanceID], fragUV).rgb;
    float opacity = clamp(median(msdf.r, msdf.g, msdf.b), 0.0, 1.0);

    outColor = vec4(vec3(1.0f), opacity);
}