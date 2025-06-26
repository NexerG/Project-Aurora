#version 450
#extension GL_EXT_nonuniform_qualifier : enable

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragInstanceID;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 2) uniform sampler2D textures[];

void main() {
    vec4 texColor = texture(textures[fragInstanceID], fragUV);
    //float edgeSmooth = fwidth(); works only with single floats, not vec4

    outColor = texColor;
}