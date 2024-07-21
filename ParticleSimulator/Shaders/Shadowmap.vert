#version 450
#extension GL_EXT_nonuniform_qualifier : require

layout(push_constant) uniform lightIndex {
    int index;
} lIndex;

layout(binding = 0) buffer lightMatrices {
    mat4 view;
    mat4 proj;
    mat4 lightProjection;
    mat4 lightView;
    vec3 camPos;
} ubo[];

layout(binding = 1) buffer instanceBuffer{
    mat4 instanceMatrices[];
};

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

void main()
{
    gl_Position = ubo[lIndex.index].proj * ubo[lIndex.index].view * (instanceMatrices[gl_InstanceIndex] * vec4(inPosition, 1.0));
}