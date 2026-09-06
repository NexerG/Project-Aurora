#version 450

layout(location = 0) in vec2 fragPos;
layout(location = 1) in flat vec4 fragClip;
layout(location = 2) in vec2 fragLocal;
layout(location = 3) in flat vec2 fragHalfExtent;
layout(location = 4) in flat vec4 fragRadius;
layout(location = 5) in flat vec4 fragTint;
layout(location = 6) in flat vec3 fragEdgeColor;
layout(location = 7) in flat float fragEdgeThickness;

layout(location = 0) out vec4 outColor;

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

    float boxDist = -sdRoundBox(fragLocal, fragHalfExtent, fragRadius);
    float boxAA = fwidth(boxDist);
    float inside = clamp(boxDist / boxAA + 0.5f, 0.0f, 1.0f);

    vec3 color = fragTint.rgb;
    float opacity = inside;

    // edge — the outermost band of the silhouette, carrying its own coverage
    if (fragEdgeThickness > 0.0f)
    {
        float band = inside - clamp((boxDist - fragEdgeThickness) / boxAA + 0.5f, 0.0f, 1.0f);
        color = mix(color, fragEdgeColor, band);
        opacity = max(opacity, band);
    }

    outColor = vec4(color, opacity * fragTint.a);
}
