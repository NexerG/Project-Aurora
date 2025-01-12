#version 460
#extension GL_EXT_ray_tracing : enable

layout(location = 0) rayPayloadInEXT vec3 hitValue;

void main()
{
    vec3 rayDir = gl_WorldRayDirectionEXT;
    vec3 up = vec3(0.0, 1.0, 0.0);

    float angleFactor = dot(normalize(rayDir), up);
    angleFactor = angleFactor * 0.5f + 0.5f;

    vec3 horizonColor = vec3(1.0, 0.7, 0.4);
    vec3 zenithColor = vec3(0.2, 0.4, 1.0);

    vec3 atmosphereColor = mix(horizonColor, zenithColor, angleFactor);

    hitValue = atmosphereColor;
}