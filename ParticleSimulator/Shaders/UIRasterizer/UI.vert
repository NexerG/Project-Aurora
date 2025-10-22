#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

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

struct Style
{
    vec3 tint;
};

layout(set = 0, binding = 2, scalar) readonly buffer vertexData {
    vec2[4] UV;
    Style style;
} VD[];

layout(location = 0) out vec2 fragUV;
layout(location = 1) out flat uint fragInstanceID;
layout(location = 2) out Style fragStyle;

void main() {
    vec3 tPos = vec3(ts.transforms[gl_InstanceIndex] * vec4(inPosition, 1.0));
    vec4 pos = ubo.proj * ubo.view * vec4(tPos, 1.0f);

    gl_Position = pos;
    fragInstanceID = gl_InstanceIndex;
    fragStyle = VD[gl_InstanceIndex].style;
    fragUV = VD[gl_InstanceIndex].UV[gl_VertexIndex];
}
