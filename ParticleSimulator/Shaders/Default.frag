#version 330 core

// Outputs colors in RGBA
out vec4 FragColor;

// Inputs the color from the Vertex Shader
in vec3 color;
// Inputs the texture coordinates from the Vertex Shader
in vec2 texCoord;

in vec3 Normal;
in vec3 crntPos;

// Gets the Texture Unit from the main function
uniform sampler2D tex0;
uniform sampler2D tex1;
//imports the light data
uniform vec4 lightColor;
uniform vec3 lightPos;
uniform vec3 camPos;

vec4 pointLight()
{
	//intensity
	vec3 lightVec = lightPos - crntPos;
	float dist = length(lightVec);
	float a = 3f;
	float b = 0.7f;
	float intensity = (1.0f /(a * dist * dist + b * dist + 1.0f)) * 1000000;
	
	float ambientLight = 0.2f;
	vec3 normal = normalize(Normal);
	vec3 lightDirection = normalize (lightPos - crntPos);
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	float specularLight = 0.5f;
	vec3 viewDirection = normalize(camPos - crntPos);
	vec3 reflectionDirection = reflect(-lightDirection, Normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	//need to add intensity back
	return (texture(tex0, texCoord) * (diffuse + ambientLight) + texture(tex1, texCoord).r * specular) * lightColor;
}

vec4 directLight()
{
	float ambientLight  = 0.5f;

	//diffuse
	vec3 normal = normalize(Normal);
	vec3 lightDirection = normalize(vec3(1.0f, 1.0f, 0.0f));
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	//specular
	float specularLight = 0.5f;
	vec3 viewDirection = normalize(camPos - crntPos);
	vec3 reflectionDirection = reflect(-lightDirection, Normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	return (texture(tex0, texCoord) * (diffuse + ambientLight) + texture(tex1, texCoord).r * specular) * lightColor;
}

vec4 spotLight()
{
	//cone setup
	float innerCone = 0.95f;
	float outterCone = 0.9f;

	//ambient
	float ambientLight = 0.5f;

	//diffuse
	vec3 normal = normalize(Normal);
	vec3 lightDirection = normalize(vec3(1.0f, 1.0f, 0.0f));
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	//specular
	float specularLight = 0.5f;
	vec3 viewDirection = normalize(camPos - crntPos);
	vec3 reflectionDirection = reflect(-lightDirection, Normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	//cone light
	float angle = dot(vec3(0.0f,-1.0f,0.0f), -lightDirection);
	float intensity = clamp((angle - outterCone) / (innerCone - outterCone), 0.0f, 1.0f);

	//need to add intensity back for the cone to appear
	return (texture(tex0, texCoord) * (diffuse * intensity + ambientLight) + texture(tex1, texCoord).r * specular * intensity) * lightColor;
}

void main()
{
	FragColor = directLight();
}