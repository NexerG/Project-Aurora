#version 460
#extension GL_EXT_ray_tracing : enable
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

layout(location = 0) rayPayloadInEXT vec3 hitValue;
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

hitAttributeEXT vec2 attribs;

void main()
{
	const vec3 barycentricCoords = vec3(1.0f - attribs.x - attribs.y, attribs.x, attribs.y);
	vec3 albedo = vec3(0.05f, 0.5f, 0.247f);

	// use when smoothed surface
	//vec3 normal = barycentricCoords.x * normal0 + barycentricCoords.y * normal1 + barycentricCoords.z * normal2;

	uint index0 = indices[gl_InstanceID].ind[gl_PrimitiveID * 3 + 0];
	uint index1 = indices[gl_InstanceID].ind[gl_PrimitiveID * 3 + 1];
	uint index2 = indices[gl_InstanceID].ind[gl_PrimitiveID * 3 + 2];

	//vec3 n0 = vertices[gl_InstanceID].vertexData[index0].normal;
	//vec3 n1 = vertices[gl_InstanceID].vertexData[index1].normal;
	//vec3 n2 = vertices[gl_InstanceID].vertexData[index2].normal;

	vec3 p0 = vertices[gl_InstanceID].vertexData[index0].pos;
	vec3 p1 = vertices[gl_InstanceID].vertexData[index1].pos;
	vec3 p2 = vertices[gl_InstanceID].vertexData[index2].pos;

	vec3 AB = p1 - p0;
	vec3 AC = p2 - p0;
	vec3 normal = normalize(cross(AB, AC));
	normal = normalize(mat3(transpose(transformations[gl_InstanceID].transform)) * normal);

	vec3 lightDir = normalize(vec3(1.0f, 0.5f, 0.0f));
	float luminosity = max(dot(normal, lightDir), 0.1f);
	vec3 diffuse = luminosity * albedo;

	hitValue = diffuse;

	float tmin = 0.001;
	float tmax = 10000.0;
	vec3 origin = gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT;
	shadowed = true;
	// Trace shadow ray and offset indices to match shadow hit/miss shader group indices
	traceRayEXT(topLevelAS, gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsOpaqueEXT | gl_RayFlagsSkipClosestHitShaderEXT, 0xFF, 0, 0, 1, origin, tmin, lightDir, tmax, 2);
	if (shadowed) {
		hitValue = hitValue * 0.2f;
	}
}