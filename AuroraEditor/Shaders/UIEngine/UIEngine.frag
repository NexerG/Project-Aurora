#version 450
#extension GL_EXT_nonuniform_qualifier : enable

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

layout(location = 0) out vec4 outColor;

// set 0 is the renderer's and set 1 this module's, so the texture table lands here
layout(set = 2, binding = 0) uniform sampler2D samplers[];

// VulkanControlType
const uint MTSDF_CONTROL = 0u;

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
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

    outColor = vec4(color, opacity * fragTint.a);
}
