#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

// rest is an inline paint word or an offset into the palette block; stepped is that offset stepped once
struct GradientStop
{
    uint rest;
    uint stepped;
    float shade;
    float alpha;
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

// the palette slots a role stop indexes
layout(set = 1, binding = 4, scalar) readonly buffer PaintBuffer {
    vec4 paints[];
} PAINT;

layout(location = 0) in vec2 fragPos;
layout(location = 1) in flat vec4 fragClip;
layout(location = 2) in vec2 fragLocal;
layout(location = 3) in flat vec2 fragHalfExtent;
layout(location = 4) in flat vec4 fragRadius;
layout(location = 5) in flat vec4 fragTint;
layout(location = 6) in flat vec3 fragEdgeColor;
layout(location = 7) in flat vec4 fragEdgeThickness;
layout(location = 8) in vec2 fragUV;
layout(location = 9) in flat uint fragTextureIndex;
layout(location = 10) in flat uint fragType;
layout(location = 11) in flat uint fragPaint;
layout(location = 12) in flat vec4 fragGradientRect;
layout(location = 13) in flat uint fragEdgePaint;

layout(location = 0) out vec4 outColor;

// set 0 is the renderer's and set 1 this module's, so the texture table lands here
layout(set = 2, binding = 0) uniform sampler2D samplers[];

// VulkanControlType
const uint MTSDF_CONTROL = 0u;
const uint PANEL_CONTROL = 1u;
const uint IMAGE_CONTROL = 2u;

// VulkanControl.noTexture
const uint NO_TEXTURE = 0xFFFFFFFFu;

bool isGradient(uint word)
{
    return (word & 0xC0000000u) == 0x40000000u;
}

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

// A stop's colour: inline, or its role in the palette block at base, shaded toward the stepped slot.
vec4 stopColor(GradientStop s, uint base)
{
    vec3 rgb;
    if ((s.rest & 0x80000000u) != 0u)
        rgb = vec3(float((s.rest >> 16) & 0xFFu), float((s.rest >> 8) & 0xFFu), float(s.rest & 0xFFu)) / 255.0f;
    else
        rgb = clamp(mix(PAINT.paints[base + s.rest].rgb, PAINT.paints[base + s.stepped].rgb, s.shade), 0.0f, 1.0f);
    return vec4(rgb, s.alpha);
}

// Ramps a gradient across rect, in the same design space as p. Linear spans the rect corner to
// corner along its direction; radial is an ellipse reaching the farthest corner. word is a gradient
// paint word: the palette's first slot in bits 29..14, the gradient id below.
vec4 sampleGradient(uint word, vec2 p, vec4 rect)
{
    Gradient g = GB.gradients[word & 0x3FFFu];
    uint base = (word >> 14) & 0xFFFFu;
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

    vec4 color = stopColor(g.stops[0], base);
    for (uint i = 1u; i < g.stopCount; ++i)
    {
        float from = g.stops[i - 1u].pos;
        float to = g.stops[i].pos;
        color = mix(color, stopColor(g.stops[i], base), clamp((t - from) / max(to - from, 1e-5f), 0.0f, 1.0f));
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
    // clip as coverage
    float inClip = (fragPos.x < fragClip.x || fragPos.y < fragClip.y ||
                    fragPos.x > fragClip.z || fragPos.y > fragClip.w) ? 0.0f : 1.0f;

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
    if (isGradient(fragPaint))
    {
        vec4 ramp = sampleGradient(fragPaint, fragPos, fragGradientRect);
        color = ramp.rgb;
        alpha *= ramp.a;
    }

    // edge — the outermost band of the silhouette, carrying its own coverage
    // widths are top, right, bottom, left; a glyph takes the widest
    vec4 widths = fragEdgeThickness;
    float widest = max(max(widths.x, widths.y), max(widths.z, widths.w));
    if (widest > 0.0f)
    {
        float inner;
        if (fragType == MTSDF_CONTROL)
            inner = (dist - widest / max(fwidth(fragPos.x), 1e-6f)) / aa;
        else
        {
            vec2 innerCentre = vec2(widths.w - widths.y, widths.x - widths.z) * 0.5f;
            vec2 innerHalf = max(fragHalfExtent - vec2(widths.w + widths.y, widths.x + widths.z) * 0.5f, vec2(0.0f));
            inner = -sdRoundBox(fragLocal - innerCentre, innerHalf, max(fragRadius - widest, vec4(0.0f))) / aa;
        }

        float band = opacity - clamp(inner + 0.5f, 0.0f, 1.0f);
        vec3 edgeColor = fragEdgeColor;
        if (isGradient(fragEdgePaint))
        {
            vec4 ramp = sampleGradient(fragEdgePaint, fragPos, fragGradientRect);
            edgeColor = ramp.rgb;
            band *= ramp.a;
        }
        color = mix(color, edgeColor, band);
        opacity = max(opacity, band);
    }

    // mask — the last silhouette, so it cuts the edge band along with the fill
    if (fragType == PANEL_CONTROL && fragTextureIndex != NO_TEXTURE)
        opacity *= clamp(msdfDistance() + 0.5f, 0.0f, 1.0f);

    outColor = vec4(color, opacity * alpha * inClip);
}
