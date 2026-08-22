#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

struct Style
{
    vec4 tint;
};

struct GradientStop
{
    vec4 color;
    float pos;
};

struct Gradient
{
    vec2 direction;
    vec2 center;
    uint kind;
    uint stopCount;
    GradientStop stops[8];
};

layout(set = 0, binding = 3, scalar) readonly buffer GradientBuffer {
    Gradient gradients[];
} GB;

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragTextureIndex;
layout(location = 2) in Style fragStyle;
layout(location = 3) in vec2 fragPos;
layout(location = 4) in flat vec4 fragClip;
layout(location = 5) in vec2 fragLocal;
layout(location = 6) in flat vec2 fragHalfExtent;
layout(location = 7) in flat vec4 fragRadius;
layout(location = 8) in flat vec3 fragEdgeColor;
layout(location = 9) in flat float fragEdgeThickness;
layout(location = 10) in flat vec3 fragOutlineColor;
layout(location = 11) in flat float fragOutlineWidth;
layout(location = 12) in flat uint fragGradientIndex;
layout(location = 13) in flat vec4 fragGradientRect;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D samplers[];

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

// Ramps a gradient across rect, in the same design space as p. Linear spans the rect corner to
// corner along its direction; radial is an ellipse reaching the farthest corner.
vec4 sampleGradient(uint index, vec2 p, vec4 rect)
{
    Gradient g = GB.gradients[index];
    vec2 extent = max((rect.zw - rect.xy) * 0.5f, vec2(1e-5f));
    vec2 local = p - (rect.xy + rect.zw) * 0.5f;

    float t;
    if (g.kind == 0u)
    {
        float span = abs(g.direction.x) * extent.x + abs(g.direction.y) * extent.y;
        t = (dot(local, g.direction) + span) / (2.0f * span);
    }
    else
    {
        vec2 offset = (g.center * 2.0f - 1.0f) * extent;
        t = length((local - offset) / (extent + abs(offset)));
    }
    t = clamp(t, 0.0f, 1.0f);

    vec4 color = g.stops[0].color;
    for (uint i = 1u; i < g.stopCount; ++i)
    {
        float from = g.stops[i - 1u].pos;
        float to = g.stops[i].pos;
        color = mix(color, g.stops[i].color, clamp((t - from) / max(to - from, 1e-5f), 0.0f, 1.0f));
    }
    return color;
}

// Signed distance to a rounded rectangle, negative inside. r is (topLeft, topRight, bottomLeft, bottomRight).
float sdRoundBox(vec2 p, vec2 b, vec4 r) {
    vec2 side = (p.y > 0.0f) ? r.zw : r.xy;
    float rad = min((p.x > 0.0f) ? side.y : side.x, min(b.x, b.y));
    vec2 q = abs(p) - b + rad;
    return min(max(q.x, q.y), 0.0f) + length(max(q, 0.0f)) - rad;
}

void main()
{
    if (fragPos.x < fragClip.x || fragPos.y < fragClip.y ||
        fragPos.x > fragClip.z || fragPos.y > fragClip.w)
        discard;

    vec4 mtsdf = texture(samplers[fragTextureIndex], fragUV);
    float msdfDist = median(mtsdf.r, mtsdf.g, mtsdf.b);
    float trueDist = mtsdf.a;

    float sd = msdfDist;
    float trueSD = trueDist;
    if (abs((sd - 0.5f) - (trueSD - 0.5f)) > 0.1f)
    {
        sd = trueSD;
    }

    float pxRange = 4.0f;
    vec2 atlasSize = vec2(textureSize(samplers[fragTextureIndex], 0));
    vec2 unitRange = vec2(pxRange) / atlasSize;
    vec2 screenTexSize = vec2(1.0f) / fwidth(fragUV);
    float screenPxRange = max(1.0f, length(unitRange * screenTexSize));

    float screenPxDist = screenPxRange * (sd - 0.5f);
    float fillAlpha = clamp(screenPxDist + 0.5f, 0.0f, 1.0f);

    vec3 color = fragStyle.tint.rgb;
    float opacity = fillAlpha;
    float gradientAlpha = 1.0f;

    // gradient — replaces the flat tint as the fill, so the outline still composites under it
    if (fragGradientIndex > 0u)
    {
        vec4 ramp = sampleGradient(fragGradientIndex, fragPos, fragGradientRect);
        color = ramp.rgb;
        gradientAlpha = ramp.a;
    }

    // outline — a second threshold that far outside the shape, the fill composited over it
    if (fragOutlineWidth > 0.0f)
    {
        opacity = clamp(screenPxDist + fragOutlineWidth + 0.5f, 0.0f, 1.0f);
        color = mix(fragOutlineColor, color, fillAlpha);
    }

    // after the outline, which assigns opacity outright rather than multiplying into it
    opacity *= gradientAlpha;

    float boxDist = -sdRoundBox(fragLocal, fragHalfExtent, fragRadius);
    float boxAA = fwidth(boxDist);
    float inside = clamp(boxDist / boxAA + 0.5f, 0.0f, 1.0f);
    opacity *= inside;

    // edge — the outermost band of the silhouette, carrying its own coverage past the mask
    if (fragEdgeThickness > 0.0f)
    {
        float band = inside - clamp((boxDist - fragEdgeThickness) / boxAA + 0.5f, 0.0f, 1.0f);
        color = mix(color, fragEdgeColor, band);
        opacity = max(opacity, band);
    }

    outColor = vec4(color, opacity * fragStyle.tint.a);
}