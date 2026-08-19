#version 450
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

struct Style
{
    vec3 tint;
};

layout(location = 0) in vec2 fragUV;
layout(location = 1) in flat uint fragTextureIndex;
layout(location = 2) in Style fragStyle;
layout(location = 3) in vec2 fragPos;
layout(location = 4) in flat vec4 fragClip;
layout(location = 5) in vec2 fragLocal;
layout(location = 6) in flat vec2 fragHalfExtent;
layout(location = 7) in flat float fragRadius;
layout(location = 8) in flat vec3 fragEdgeColor;
layout(location = 9) in flat float fragEdgeThickness;
layout(location = 10) in flat vec3 fragOutlineColor;
layout(location = 11) in flat float fragOutlineWidth;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D samplers[];

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

// Signed distance to a rounded rectangle, negative inside.
float sdRoundBox(vec2 p, vec2 b, float r) {
    r = min(r, min(b.x, b.y));
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0f) + length(max(q, 0.0f)) - r;
}

void main()
{
    if (fragPos.x < fragClip.x || fragPos.y < fragClip.y ||
        fragPos.x > fragClip.z || fragPos.y > fragClip.w)
        discard;

    vec3 msdf = texture(samplers[fragTextureIndex], fragUV).rgb;
    float sd = median(msdf.r, msdf.g, msdf.b) - 0.5f;
    float screenPxRange = fwidth(sd);
    float screenPxDist = sd / screenPxRange;
    float fillAlpha = clamp(screenPxDist + 0.5f, 0.0f, 1.0f);
    //float opacity = smoothstep(-screenPxRange, screenPxRange, sd);

    vec3 color = fragStyle.tint;
    float opacity = fillAlpha;

    // outline — a second threshold that far outside the shape, the fill composited over it
    if (fragOutlineWidth > 0.0f)
    {
        opacity = clamp(screenPxDist + fragOutlineWidth + 0.5f, 0.0f, 1.0f);
        color = mix(fragOutlineColor, color, fillAlpha);
    }

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

    outColor = vec4(color, opacity);
}