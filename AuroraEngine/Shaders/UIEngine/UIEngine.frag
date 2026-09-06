#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

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

// uploaded once at bootstrap and shared by every window, so a definition is named rather than copied
layout(set = 1, binding = 3, scalar) readonly buffer GradientBuffer {
    Gradient gradients[];
} GB;

layout(location = 0) in vec2 fragPos;
layout(location = 1) in flat vec4 fragClip;
layout(location = 2) in vec2 fragLocal;
layout(location = 3) in flat vec2 fragHalfExtent;
layout(location = 4) in flat vec4 fragRadius;
layout(location = 5) in flat vec4 fragTint;
layout(location = 6) in flat vec3 fragEdgeColor;
layout(location = 7) in flat float fragEdgeThickness;
layout(location = 8) in vec2 fragUV;
layout(location = 9) in flat uint fragTextureIndex;
layout(location = 10) in flat uint fragType;
layout(location = 11) in flat uint fragGradientIndex;
layout(location = 12) in flat vec4 fragGradientRect;

layout(location = 0) out vec4 outColor;

// set 0 is the renderer's and set 1 this module's, so the texture table lands here
layout(set = 2, binding = 0) uniform sampler2D samplers[];

// VulkanControlType
const uint MTSDF_CONTROL = 0u;
const uint PANEL_CONTROL = 1u;
const uint IMAGE_CONTROL = 2u;

// VulkanControl.noTexture
const uint NO_TEXTURE = 0xFFFFFFFFu;

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

// Screen-pixel distance from the glyph silhouette, positive inside.
float msdfDistance()
{
    vec4 mtsdf = texture(samplers[fragTextureIndex], fragUV);
    float sd = median(mtsdf.r, mtsdf.g, mtsdf.b);
    float trueSD = mtsdf.a;

    // The true distance field wins where the median disagrees with it — a corner the three
    // channels resolve differently is where an MSDF alone produces a notch.
    if (abs((sd - 0.5f) - (trueSD - 0.5f)) > 0.1f)
        sd = trueSD;

    // Must match MTSDFGen.PxRange. Cells are normalized per glyph against max(w, h), so the range
    // holds on the long axis only and the larger of the two components is the one to scale by.
    float pxRange = 4.0f;
    vec2 atlasSize = vec2(textureSize(samplers[fragTextureIndex], 0));
    vec2 unitRange = vec2(pxRange) / atlasSize;
    vec2 screenTexSize = vec2(1.0f) / fwidth(fragUV);
    vec2 rangeInPx = unitRange * screenTexSize;
    float screenPxRange = max(1.0f, max(rangeInPx.x, rangeInPx.y));

    return screenPxRange * (sd - 0.5f);
}

void main()
{
    if (fragPos.x < fragClip.x || fragPos.y < fragClip.y ||
        fragPos.x > fragClip.z || fragPos.y > fragClip.w)
        discard;

    vec3 color = fragTint.rgb;
    float alpha = fragTint.a;
    float opacity;
    float dist;
    float aa;

    // edgeThickness is design pixels for both kinds, so each distance is banded in its own units:
    // the analytic box is already in design space, the glyph's is in screen pixels
    if (fragType == MTSDF_CONTROL)
    {
        dist = msdfDistance();
        aa = 1.0f;
        opacity = clamp(dist + 0.5f, 0.0f, 1.0f);
    }
    else
    {
        dist = -sdRoundBox(fragLocal, fragHalfExtent, fragRadius);
        aa = fwidth(dist);
        opacity = clamp(dist / aa + 0.5f, 0.0f, 1.0f);

        if (fragType == IMAGE_CONTROL && fragTextureIndex != NO_TEXTURE)
        {
            vec4 texel = texture(samplers[fragTextureIndex], fragUV);
            color *= texel.rgb;
            alpha *= texel.a;
        }
    }

    // gradient — replaces the fill, so the edge still bands over it
    if (fragGradientIndex > 0u)
    {
        vec4 ramp = sampleGradient(fragGradientIndex, fragPos, fragGradientRect);
        color = ramp.rgb;
        alpha *= ramp.a;
    }

    // edge — the outermost band of the silhouette, carrying its own coverage
    if (fragEdgeThickness > 0.0f)
    {
        float thickness = fragType == MTSDF_CONTROL
            ? fragEdgeThickness / max(fwidth(fragPos.x), 1e-6f)
            : fragEdgeThickness;

        float band = opacity - clamp((dist - thickness) / aa + 0.5f, 0.0f, 1.0f);
        color = mix(color, fragEdgeColor, band);
        opacity = max(opacity, band);
    }

    // mask — the last silhouette, so it cuts the edge band along with the fill
    if (fragType == PANEL_CONTROL && fragTextureIndex != NO_TEXTURE)
        opacity *= clamp(msdfDistance() + 0.5f, 0.0f, 1.0f);

    outColor = vec4(color, opacity * alpha);
}
