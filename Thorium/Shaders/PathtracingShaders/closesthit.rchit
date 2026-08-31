#version 460
#extension GL_EXT_ray_tracing : enable
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

struct RayPayload
{
	vec3 hitColor;
	vec3 incLight;
	vec3 hitPos;
	float distance;
	vec3 normal;
	float reflector;
	bool hitAtmosphere;
};

layout(location = 0) rayPayloadInEXT RayPayload payload;
layout(location = 2) rayPayloadEXT bool shadowed;
layout(binding = 0, set = 0) uniform accelerationStructureEXT topLevelAS;

struct Vertex{
	vec3 pos;
	vec3 normal;
	vec2 uv;
};

layout(set = 0, scalar, binding = 3) buffer vertexBuffer {
	Vertex vertexData[];
} vertices[];

layout(set = 0, scalar, binding = 4) buffer index {
	uint ind[];
} indices[];

layout(set = 0, scalar, binding = 5) uniform Transform{
	mat3x4 transform;
} transformations[];

layout(set = 0, scalar, binding = 6) uniform Color{
	vec3 color;
} colors[];

hitAttributeEXT vec2 attribs;

void main()
{
	const vec3 barycentricCoords = vec3(1.0f - attribs.x - attribs.y, attribs.x, attribs.y);

	uint index0 = indices[gl_InstanceID].ind[gl_PrimitiveID * 3 + 0];
	uint index1 = indices[gl_InstanceID].ind[gl_PrimitiveID * 3 + 1];
	uint index2 = indices[gl_InstanceID].ind[gl_PrimitiveID * 3 + 2];

	vec3 p0 = vertices[gl_InstanceID].vertexData[index0].pos;
	vec3 p1 = vertices[gl_InstanceID].vertexData[index1].pos;
	vec3 p2 = vertices[gl_InstanceID].vertexData[index2].pos;

	vec3 AB = p1 - p0;
	vec3 AC = p2 - p0;
	vec3 normal = normalize(cross(AB, AC));
	normal = normalize(mat3(transpose(transformations[gl_InstanceID].transform)) * normal);

	//vec3 emittedLight = ; ONLY WHEN I WILL IMPLEMENT EMISSIVE MATERIALS

	payload.normal = normal;
	payload.hitPos = (gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT);
	payload.distance = gl_HitTEXT;

	float tmin = 0.001;
	float tmax = 10000.0;
	vec3 origin = gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT;
	shadowed = true;
	// Trace shadow ray and offset indices to match shadow hit/miss shader group indices
	traceRayEXT(topLevelAS, gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsOpaqueEXT | gl_RayFlagsSkipClosestHitShaderEXT, 0xFF, 0, 0, 1, origin, tmin, lightDir, tmax, 2);
	if (shadowed)
	{
		payload.hitColor += (lerp(colors[gl_InstanceID], vec3(0.75f, 0.75f, 0.75f), 0) * 0.25);
	}
	else
	{
		payload.hitColor += lerp(colors[gl_InstanceID], vec3(0.75f, 0.75f, 0.75f), 0);
	}
	payload.hitAtmosphere = false;
}