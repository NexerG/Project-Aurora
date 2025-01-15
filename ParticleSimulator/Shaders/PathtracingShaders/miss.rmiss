#version 460
#extension GL_EXT_ray_tracing : enable

struct RayPayload
{
	vec3 hitColor;
	vec3 hitPos;
    float distance;
	vec3 normal;
	float reflector;
    bool hitAtmosphere;
};

layout(location = 0) rayPayloadInEXT RayPayload payload;

void main()
{
    vec3 rayDir = gl_WorldRayDirectionEXT;
    vec3 up = vec3(0.0, 1.0, 0.0);

    float angleFactor = dot(normalize(rayDir), up);
    angleFactor = angleFactor * 0.5f + 0.5f;

    vec3 horizonColor = vec3(1.0, 0.7, 0.4);
    vec3 zenithColor = vec3(0.2, 0.4, 1.0);

    vec3 atmosphereColor = mix(horizonColor, zenithColor, angleFactor);

    payload.hitColor = atmosphereColor;
    payload.hitAtmosphere = true;
}