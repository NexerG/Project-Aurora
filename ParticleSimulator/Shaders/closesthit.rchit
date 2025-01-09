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
hitAttributeEXT vec2 attribs;

void main()
{
	const vec3 barycentricCoords = vec3(1.0f - attribs.x - attribs.y, attribs.x, attribs.y);
	vec3 albedo = vec3(0.05f, 0.5f, 0.247f);

	// use when smoothed surface
	//vec3 normal = barycentricCoords.x * normal0 + barycentricCoords.y * normal1 + barycentricCoords.z * normal2;

	uint index0 = ind[3 * gl_PrimitiveID];
	uint index1 = ind[3 * gl_PrimitiveID + 1];
	uint index2 = ind[3 * gl_PrimitiveID + 2];

	vec3 n0 = vertexData[index0].normal;
	vec3 n1 = vertexData[index1].normal;
	vec3 n2 = vertexData[index2].normal;

	vec3 normal = normalize(n0 + n1 + n2);

	vec3 lightDir = normalize(vec3(1.0f, 1.0f, 0.0f));
	float luminosity = max(dot(normal, lightDir), 0.2f);
	vec3 diffuse = luminosity * albedo;

	hitValue = diffuse;
}