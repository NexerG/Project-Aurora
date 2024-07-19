#version 450

layout(binding = 0) uniform UniformBufferObject {
    mat4 view;
    mat4 proj;
    mat4 lightProjection;
    mat4 lightView;
    vec3 camPos;
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
layout(location = 3) out vec4 fragPosLight;
layout(location = 4) out vec3 cp;

void main()
{
    mat3 normMat = inverse(mat3(instanceMatrices[gl_InstanceIndex]));
    currentPos = vec3(instanceMatrices[gl_InstanceIndex] * vec4(inPosition, 1.0));
    fragPosLight = ubo.lightProjection  * ubo.lightView * mat4(1.0f) * vec4(currentPos, 1.0f);
    texCoord = inUV;
    normal = normalize(normMat * inNormal);
    cp = ubo.camPos;
    gl_Position = ubo.proj * ubo.view * instanceMatrices[gl_InstanceIndex] * vec4(inPosition, 1.0);
}