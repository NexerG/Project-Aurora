#version 450

layout(binding = 0) uniform UniformBufferObject {
    mat4 view;
    mat4 proj;
} ubo;

layout(binding = 1) buffer instanceBuffer{
    mat4 instanceMatrices[];
};

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

layout(location = 0) out vec2 texCoord;
layout(location = 1) out vec3 normal;
layout(location = 2) out vec3 currentPos;

void main()
{
    currentPos = vec3(instanceMatrices[gl_InstanceIndex] * vec4(inPosition, 1.0));
    gl_Position = ubo.proj * ubo.view * instanceMatrices[gl_InstanceIndex] * vec4(inPosition, 1.0);
    texCoord = inUV;
    normal = normalize(inNormal);
}