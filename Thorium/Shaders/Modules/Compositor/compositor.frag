#version 450
layout(constant_id = 0) const int MODULE_COUNT = 1;
layout(set = 0, binding = 0) uniform sampler2D moduleOutputs[MODULE_COUNT];
layout(location = 0) in  vec2 inUV;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 result = vec4(0.0, 0.0, 0.0, 0.0);
    for (int i = 0; i < MODULE_COUNT; i++)
    {
        vec4 src = texture(moduleOutputs[i], inUV);
        // standard over operator, back to front
        result = src + result * (1.0 - src.a);
    }
    outColor = result;
}