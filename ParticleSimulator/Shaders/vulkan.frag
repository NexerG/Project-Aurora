#version 450

struct LightData {
    vec3 pos;
    vec4 color;
};

layout(binding = 2) uniform lightUniform {
    LightData lights[];
    int lightCount;
	vec3 camPos;
} LUniform;

layout(binding = 3) uniform sampler2D texSampler;

layout(location = 0) in vec2 texCoord;
layout(location = 1) in vec3 normal;
layout(location = 2) in vec3 currentPos;

layout(location = 0) out vec4 outColor;

vec4 pointLight(vec3 lightPosition)
{
	//intensity
	vec3 lightVec = lightPosition - currentPos;
	float dist = length(lightVec);
	float a = 3.0f;
	float b = 0.7f;
	float intensity = (1.0f /(a * dist * dist + b * dist + 1.0f)) * 3000.0f;
	
	float ambientLight = 0.20f;

	vec3 lightDirection = normalize(lightVec);
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	float specular = 0.0f;
	if(diffuse != 0.0f)
	{
		float specularLight = 0.50f;
		vec3 viewDirection = normalize(LUniform.camPos - currentPos);
		vec3 halfWayVec = normalize(viewDirection + lightDirection);
		float specAmount = pow(max(dot(normal, halfWayVec), 0.0f), 16);
		specular = specAmount * specularLight;
	}
	return (texture(texSampler, texCoord) * (diffuse * intensity + ambientLight)  + texture(texSampler, texCoord).r * specular * intensity) * vec4(1,1,1,1);
}

vec4 directLight()
{
	float ambientLight  = 0.2f;

	//diffuse
	vec3 lightDirection = normalize(vec3(1.0f, 1.0f, 0.0f));
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	//specular
	float specularLight = 0.5f;
	vec3 viewDirection = normalize(LUniform.camPos - currentPos);
	vec3 reflectionDirection = reflect(-lightDirection, normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	return ((diffuse + ambientLight) * texture(texSampler, texCoord) + specular * texture(texSampler, texCoord).r) * vec4(1,1,1,1);
}

vec4 spotLight()
{
	//cone setup
	float innerCone = 0.95f;
	float outterCone = 0.9f;

	//ambient
	float ambientLight = 0.5f;

	//diffuse
	vec3 lightDirection = normalize(vec3(1.0f, 1.0f, 0.0f));
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	//specular
	float specularLight = 0.5f;
	vec3 viewDirection = normalize(LUniform.camPos - currentPos);
	vec3 reflectionDirection = reflect(-lightDirection, normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	//cone light
	float angle = dot(vec3(0.0f,-1.0f,0.0f), -lightDirection);
	float intensity = clamp((angle - outterCone) / (innerCone - outterCone), 0.0f, 1.0f);

	//need to add intensity back for the cone to appear
	return (texture(texSampler, texCoord) * (diffuse * intensity + ambientLight) + texture(texSampler, texCoord).r * specular * intensity) * vec4(1,1,1,1);
}


void main()
{
    //outColor = texture(texSampler, texCoord);
	outColor = pointLight(LUniform.lights[0].pos);
}