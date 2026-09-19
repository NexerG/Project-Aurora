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
    uint paint;
    float alpha;
    uint textureIndex;
    vec4 cornerRadius;
    uint edgePaint;
    vec4 edgeThickness;
    float state;
    uint effect;
    float effectStart;
};

// 96-byte stride, same reasoning as above
layout(set = 1, binding = 2, scalar) readonly buffer ControlBuffer {
    VulkanControl rows[];
} CTRL;

struct Effect
{
    vec2 offsetFrom;
    vec2 offsetTo;
    float scaleFrom;
    float scaleTo;
    float rotateFrom;
    float rotateTo;
    float alphaFrom;
    float alphaTo;
    float duration;
    float stagger;
    uint loop;
    uint ease;
    uint steps;
    vec4 bezier;
};

// named effects, evaluated against engine time; row 0 is none
layout(set = 1, binding = 5, scalar) readonly buffer EffectBuffer {
    Effect effects[];
} FX;

// EaseKind: Linear, then 10 families × In/Out/InOut, then CubicBezier, Steps — mirrors Curve.Evaluate
const uint EASE_CUBIC_BEZIER = 31u;
const uint EASE_STEPS = 32u;
const float PI = 3.14159265f;

float bounceOut(float t)
{
    const float n1 = 7.5625f;
    const float d1 = 2.75f;
    if (t < 1.0f / d1) return n1 * t * t;
    if (t < 2.0f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
    if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
    t -= 2.625f / d1;
    return n1 * t * t + 0.984375f;
}

float easeIn(uint family, float t)
{
    const float c1 = 1.70158f;
    const float c4 = 2.0f * PI / 3.0f;
    switch (family)
    {
        case 0u: return 1.0f - cos(t * PI * 0.5f);
        case 1u: return t * t;
        case 2u: return t * t * t;
        case 3u: return t * t * t * t;
        case 4u: return t * t * t * t * t;
        case 5u: return t == 0.0f ? 0.0f : pow(2.0f, 10.0f * t - 10.0f);
        case 6u: return 1.0f - sqrt(max(1.0f - t * t, 0.0f));
        case 7u: return (c1 + 1.0f) * t * t * t - c1 * t * t;
        case 8u: return (t == 0.0f || t == 1.0f) ? t : -pow(2.0f, 10.0f * t - 10.0f) * sin((10.0f * t - 10.75f) * c4);
        default: return 1.0f - bounceOut(1.0f - t);
    }
}

float bezierCubic(float p1, float p2, float s)
{
    return ((1.0f - 3.0f * p2 + 3.0f * p1) * s + (3.0f * p2 - 6.0f * p1)) * s * s + 3.0f * p1 * s;
}

float bezierEase(vec4 p, float t)
{
    float lo = 0.0f, hi = 1.0f, s = t;
    for (int i = 0; i < 24; i++)
    {
        float x = bezierCubic(p.x, p.z, s);
        if (abs(x - t) < 1e-5f) break;
        if (x < t) lo = s; else hi = s;
        s = (lo + hi) * 0.5f;
    }
    return bezierCubic(p.y, p.w, s);
}

float ease(Effect e, float t)
{
    t = clamp(t, 0.0f, 1.0f);
    if (e.ease == 0u) return t;
    if (e.ease == EASE_CUBIC_BEZIER) return bezierEase(e.bezier, t);
    if (e.ease == EASE_STEPS) return t >= 1.0f ? 1.0f : floor(t * float(e.steps)) / float(e.steps);

    uint family = (e.ease - 1u) / 3u;
    uint mode = (e.ease - 1u) % 3u;
    if (mode == 0u) return easeIn(family, t);
    if (mode == 1u) return 1.0f - easeIn(family, 1.0f - t);
    return t < 0.5f ? easeIn(family, 2.0f * t) * 0.5f : 1.0f - easeIn(family, 2.0f - 2.0f * t) * 0.5f;
}

// Progress through an effect's cycle for Once, Loop or PingPong.
float effectProgress(Effect e, float start)
{
    float t = max(engine.totalTime - start, 0.0f) / max(e.duration, 1e-5f);
    if (e.loop == 1u) return fract(t);
    if (e.loop == 2u) return 1.0f - abs(1.0f - mod(t, 2.0f));
    return min(t, 1.0f);
}

// the palette slots a paint word indexes
layout(set = 1, binding = 4, scalar) readonly buffer PaintBuffer {
    vec4 paints[];
} PAINT;

// A paint word: 0xRRGGBB with the top bit set, a gradient with the next bit set, or a slot.
// A gradient resolves to white here and is ramped per fragment.
vec4 resolvePaint(uint word)
{
    if ((word & 0x80000000u) != 0u)
        return vec4(float((word >> 16) & 0xFFu), float((word >> 8) & 0xFFu), float(word & 0xFFu), 255.0f) / 255.0f;
    if ((word & 0x40000000u) != 0u)
        return vec4(1.0f);
    return PAINT.paints[word];
}

// A slot word with a state above 0 blends toward its hover slot, then its press slot.
vec4 resolveStatePaint(uint word, float state)
{
    if (state <= 0.0f || (word & 0xC0000000u) != 0u)
        return resolvePaint(word);
    float s = min(state, 2.0f);
    if (s <= 1.0f)
        return mix(PAINT.paints[word], PAINT.paints[word + 1u], s);
    return mix(PAINT.paints[word + 1u], PAINT.paints[word + 2u], s - 1.0f);
}

layout(location = 0) out vec2 fragPos;
layout(location = 1) out flat vec4 fragClip;
layout(location = 2) out vec2 fragLocal;
layout(location = 3) out flat vec2 fragHalfExtent;
layout(location = 4) out flat vec4 fragRadius;
layout(location = 5) out flat vec4 fragTint;
layout(location = 6) out flat vec3 fragEdgeColor;
layout(location = 7) out flat vec4 fragEdgeThickness;
layout(location = 8) out vec2 fragUV;
layout(location = 9) out flat uint fragTextureIndex;
layout(location = 10) out flat uint fragType;
layout(location = 11) out flat uint fragPaint;
layout(location = 12) out flat vec4 fragGradientRect;
layout(location = 13) out flat uint fragEdgePaint;

void main() {
    mat4 model = GEO.rows[gl_InstanceIndex].matrix;
    vec3 tPos = vec3(model * vec4(inPosition, 1.0f));

    // pixels from the control's centre in design space, where +y is down and corners are named
    vec2 size = vec2(length(model[0].xyz), length(model[1].xyz));
    fragLocal = tPos.xy - model[3].xy;
    fragHalfExtent = size * 0.5f;

    // effect — scale and rotate about the centre, then offset; the SDF stays in the quad's own frame
    float effectAlpha = 1.0f;
    uint effect = CTRL.rows[gl_InstanceIndex].effect;
    if (effect > 0u)
    {
        Effect e = FX.effects[effect];
        float k = ease(e, effectProgress(e, CTRL.rows[gl_InstanceIndex].effectStart));
        float scale = mix(e.scaleFrom, e.scaleTo, k);
        float angle = mix(e.rotateFrom, e.rotateTo, k);
        vec2 local = fragLocal * scale;
        fragLocal = local;
        fragHalfExtent *= scale;
        vec2 turned = vec2(local.x * cos(angle) - local.y * sin(angle), local.x * sin(angle) + local.y * cos(angle));
        tPos.xy = model[3].xy + mix(e.offsetFrom, e.offsetTo, k) + turned;
        effectAlpha = mix(e.alphaFrom, e.alphaTo, k);
    }
    gl_Position = ubo.proj * ubo.view * vec4(tPos, 1.0f);

    // pre-projection, so it shares a space with the clip rect the arrange pass wrote
    fragPos = tPos.xy;
    fragClip = GEO.rows[gl_InstanceIndex].clip;

    fragRadius = CTRL.rows[gl_InstanceIndex].cornerRadius;
    fragTint = resolveStatePaint(CTRL.rows[gl_InstanceIndex].paint, CTRL.rows[gl_InstanceIndex].state) * vec4(1.0f, 1.0f, 1.0f, CTRL.rows[gl_InstanceIndex].alpha * effectAlpha);
    fragEdgeColor = resolvePaint(CTRL.rows[gl_InstanceIndex].edgePaint).rgb;
    fragEdgeThickness = CTRL.rows[gl_InstanceIndex].edgeThickness;

    // the atlas cell, cut per row — a text run hands each of its glyphs a different one
    fragUV = CTRL.rows[gl_InstanceIndex].uvs[gl_VertexIndex];
    fragTextureIndex = CTRL.rows[gl_InstanceIndex].textureIndex;
    fragType = CTRL.rows[gl_InstanceIndex].type;

    // shares a space with fragPos, so a run can hand its glyphs a rect wider than their own cells
    fragPaint = CTRL.rows[gl_InstanceIndex].paint;
    fragEdgePaint = CTRL.rows[gl_InstanceIndex].edgePaint;
    fragGradientRect = GEO.rows[gl_InstanceIndex].gradientRect;
}
