#version 460
#extension GL_EXT_ray_tracing : enable
#extension GL_EXT_nonuniform_qualifier : enable
#extension GL_EXT_scalar_block_layout : enable

layout(location = 0) rayPayloadInEXT vec3 hitValue;

struct Vertex{
	vec3 pos;
	vec3 normal;
	vec2 uv;
};

layout(scalar, binding = 3) buffer vertexBuffer {
	Vertex vertexData[];
};

layout(binding = 4) buffer index {
	uint ind[];
};

layout(binding = 5) uniform Transform{
	mat3x4 transform;
};

hitAttributeEXT vec2 attribs;

void main()
{
	const vec3 barycentricCoords = vec3(1.0f - attribs.x - attribs.y, attribs.x, attribs.y);
	vec3 albedo = vec3(0.05f, 0.5f, 0.247f);

	// use when smoothed surface
	//vec3 normal = barycentricCoords.x * normal0 + barycentricCoords.y * normal1 + barycentricCoords.z * normal2;

	uint index0 = ind[gl_PrimitiveID * 3 + 0];
	uint index1 = ind[gl_PrimitiveID * 3 + 1];
	uint index2 = ind[gl_PrimitiveID * 3 + 2];

	//vec3 n0 = vertexData[index0].normal;
	//vec3 n1 = vertexData[index1].normal;
	//vec3 n2 = vertexData[index2].normal;

	vec3 p0 = vertexData[index0].pos;
	vec3 p1 = vertexData[index1].pos;
	vec3 p2 = vertexData[index2].pos;

	vec3 AB = p1 - p0;
	vec3 AC = p2 - p0;
	vec3 normal = cross(AB, AC);
	normal = normalize(mat3(transpose(transform)) * normal);

	vec3 lightDir = normalize(vec3(1.0f, 0.5f, 0.0f));
	float luminosity = max(dot(normal, lightDir), 0.1f);
	vec3 diffuse = luminosity * albedo;

	hitValue = diffuse;
}