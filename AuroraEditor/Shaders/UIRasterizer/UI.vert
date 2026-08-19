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

struct ControlData
{
    vec2[4] UV;
    Style style;
    uint textureIndex;
    vec4 clip;
    float cornerRadius;
    vec3 edgeColor;
    float edgeThickness;
    vec3 outlineColor;
    float outlineWidth;
};

// One buffer indexed by instance, not one buffer per control. `scalar` layout is load-bearing:
// it gives this struct a stride of 100 bytes, matching the Pack=1 C# ControlData exactly. Any
// mismatch here and every control past the first reads shifted data.
layout(set = 0, binding = 2, scalar) readonly buffer ControlDataBuffer {
    ControlData controls[];
} CD;

layout(location = 0) out vec2 fragUV;
layout(location = 1) out flat uint fragTextureIndex;
layout(location = 2) out Style fragStyle;
layout(location = 3) out vec2 fragPos;
layout(location = 4) out flat vec4 fragClip;
layout(location = 5) out vec2 fragLocal;
layout(location = 6) out flat vec2 fragHalfExtent;
layout(location = 7) out flat float fragRadius;
layout(location = 8) out flat vec3 fragEdgeColor;
layout(location = 9) out flat float fragEdgeThickness;
layout(location = 10) out flat vec3 fragOutlineColor;
layout(location = 11) out flat float fragOutlineWidth;

void main() {
    mat4 model = ts.transforms[gl_InstanceIndex];
    vec3 tPos = vec3(model * vec4(inPosition, 1.0));
    vec4 pos = ubo.proj * ubo.view * vec4(tPos, 1.0f);

    gl_Position = pos;
    fragTextureIndex = CD.controls[gl_InstanceIndex].textureIndex;
    fragStyle = CD.controls[gl_InstanceIndex].style;
    fragUV = CD.controls[gl_InstanceIndex].UV[gl_VertexIndex];
    // pre-projection, so it shares a space with the clip rect the layout wrote
    fragPos = tPos.xy;
    fragClip = CD.controls[gl_InstanceIndex].clip;

    // the quad is a unit +-0.5 square, so local space is pixels from the control's centre
    vec2 size = vec2(length(model[0].xyz), length(model[1].xyz));
    fragLocal = inPosition.xy * size;
    fragHalfExtent = size * 0.5f;
    fragRadius = CD.controls[gl_InstanceIndex].cornerRadius;
    fragEdgeColor = CD.controls[gl_InstanceIndex].edgeColor;
    fragEdgeThickness = CD.controls[gl_InstanceIndex].edgeThickness;
    fragOutlineColor = CD.controls[gl_InstanceIndex].outlineColor;
    fragOutlineWidth = CD.controls[gl_InstanceIndex].outlineWidth;
}
