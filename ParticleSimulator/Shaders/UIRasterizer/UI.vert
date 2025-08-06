#version 450
#extension GL_EXT_nonuniform_qualifier : enable

layout(location = 0) in vec3 inPosition;   // Vertex position
layout(location = 1) in vec3 inNormals;    // Vertex Normal
layout(location = 2) in vec2 inUV;         // Texture coordinates

layout(set = 0, binding = 0) uniform UBO {
    mat4 view;
    mat4 proj;
} ubo;

layout(set = 0, binding = 1) readonly buffer Transform{
    mat4 transforms[];
}ts;

layout(set = 0, binding = 3) readonly buffer UVs {
    vec2[] UV;
} UVData[];

layout(location = 0) out vec2 fragUV;
layout(location = 1) out flat uint fragInstanceID;

void main() {
    vec3 tPos = vec3(ts.transforms[gl_InstanceIndex] * vec4(inPosition, 1.0));
    vec4 pos = ubo.proj * ubo.view * vec4(tPos, 1.0f);

    gl_Position = pos;
    fragInstanceID = gl_InstanceIndex;

    fragUV = UVData[gl_InstanceIndex].UV[gl_VertexIndex];
}
