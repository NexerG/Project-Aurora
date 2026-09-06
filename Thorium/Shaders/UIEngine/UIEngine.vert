#version 450
#extension GL_EXT_scalar_block_layout : enable

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormals;
layout(location = 2) in vec2 inUV;

// set 0 belongs to the renderer and is the same in every pipeline; the module's own sets follow it
layout(set = 0, binding = 0) uniform EngineStats {
    float mainTickMs;
    float physicsTickMs;
    float renderTickMs;
    float totalTime;
    float wrappedTime;
    uint frameIndex;
} engine;

layout(set = 1, binding = 0) uniform UBO {
    mat4 view;
    mat4 proj;
} ubo;

struct ControlGeometry
{
    mat4 matrix;
    vec4 clip;
    vec4 gradientRect;
};

// `scalar` layout is load-bearing: it gives this struct a stride of 96 bytes, matching the Pack=1
// C# ControlGeometry exactly. Any mismatch and every row past the first reads shifted data.
layout(set = 1, binding = 1, scalar) readonly buffer GeometryBuffer {
    ControlGeometry rows[];
} GEO;

struct VulkanControl
{
    uint type;
    vec2[4] uvs;
    vec4 tint;
    uint textureIndex;
    vec4 cornerRadius;
    vec3 edgeColor;
    float edgeThickness;
    uint gradientIndex;
};

// 92-byte stride, same reasoning as above
layout(set = 1, binding = 2, scalar) readonly buffer ControlBuffer {
    VulkanControl rows[];
} CTRL;

layout(location = 0) out vec2 fragPos;
layout(location = 1) out flat vec4 fragClip;
layout(location = 2) out vec2 fragLocal;
layout(location = 3) out flat vec2 fragHalfExtent;
layout(location = 4) out flat vec4 fragRadius;
layout(location = 5) out flat vec4 fragTint;
layout(location = 6) out flat vec3 fragEdgeColor;
layout(location = 7) out flat float fragEdgeThickness;
layout(location = 8) out vec2 fragUV;
layout(location = 9) out flat uint fragTextureIndex;
layout(location = 10) out flat uint fragType;
layout(location = 11) out flat uint fragGradientIndex;
layout(location = 12) out flat vec4 fragGradientRect;

void main() {
    mat4 model = GEO.rows[gl_InstanceIndex].matrix;
    vec3 tPos = vec3(model * vec4(inPosition, 1.0f));
    gl_Position = ubo.proj * ubo.view * vec4(tPos, 1.0f);

    // pre-projection, so it shares a space with the clip rect the arrange pass wrote
    fragPos = tPos.xy;
    fragClip = GEO.rows[gl_InstanceIndex].clip;

    // pixels from the control's centre in design space, where +y is down and corners are named
    vec2 size = vec2(length(model[0].xyz), length(model[1].xyz));
    fragLocal = tPos.xy - model[3].xy;
    fragHalfExtent = size * 0.5f;

    fragRadius = CTRL.rows[gl_InstanceIndex].cornerRadius;
    fragTint = CTRL.rows[gl_InstanceIndex].tint;
    fragEdgeColor = CTRL.rows[gl_InstanceIndex].edgeColor;
    fragEdgeThickness = CTRL.rows[gl_InstanceIndex].edgeThickness;

    // the atlas cell, cut per row — a text run hands each of its glyphs a different one
    fragUV = CTRL.rows[gl_InstanceIndex].uvs[gl_VertexIndex];
    fragTextureIndex = CTRL.rows[gl_InstanceIndex].textureIndex;
    fragType = CTRL.rows[gl_InstanceIndex].type;

    // shares a space with fragPos, so a run can hand its glyphs a rect wider than their own cells
    fragGradientIndex = CTRL.rows[gl_InstanceIndex].gradientIndex;
    fragGradientRect = GEO.rows[gl_InstanceIndex].gradientRect;
}
