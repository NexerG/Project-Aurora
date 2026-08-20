#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

struct Style
{
    vec4 tint;
};

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

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D samplers[];

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

// Signed distance to a rounded rectangle, negative inside. r is (topLeft, topRight, bottomLeft, bottomRight).
float sdRoundBox(vec2 p, vec2 b, vec4 r) {
    vec2 side = (p.y > 0.0) ? r.zw : r.xy;
    float rad = min((p.x > 0.0) ? side.y : side.x, min(b.x, b.y));
    vec2 q = abs(p) - b + rad;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - rad;
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
    if(abs((sd - 0.5f) - (trueSD - 0.5f)) > 0.1f)
    {
        sd = trueSD;
    }

    float pxRange = 4.0;
    vec2 atlasSize = vec2(textureSize(samplers[fragTextureIndex], 0));
    vec2 unitRange = vec2(pxRange) / atlasSize;
    vec2 screenTexSize = vec2(1.0) / fwidth(fragUV);
    float screenPxRange = max(1.0, length(unitRange * screenTexSize));

    float screenPxDist = screenPxRange * (sd - 0.5);
    float fillAlpha = clamp(screenPxDist + 0.5, 0.0, 1.0);

    vec3 color = fragStyle.tint.rgb;
    float opacity = fillAlpha;

    // outline — a second threshold that far outside the shape, the fill composited over it
    if (fragOutlineWidth > 0.0)
    {
        opacity = clamp(screenPxDist + fragOutlineWidth + 0.5, 0.0, 1.0);
        color = mix(fragOutlineColor, color, fillAlpha);
    }

    float boxDist = -sdRoundBox(fragLocal, fragHalfExtent, fragRadius);
    float boxAA = fwidth(boxDist);
    float inside = clamp(boxDist / boxAA + 0.5, 0.0, 1.0);
    opacity *= inside;

    // edge — the outermost band of the silhouette, carrying its own coverage past the mask
    if (fragEdgeThickness > 0.0)
    {
        float band = inside - clamp((boxDist - fragEdgeThickness) / boxAA + 0.5, 0.0, 1.0);
        color = mix(color, fragEdgeColor, band);
        opacity = max(opacity, band);
    }

    outColor = vec4(color, opacity * fragStyle.tint.a);
}