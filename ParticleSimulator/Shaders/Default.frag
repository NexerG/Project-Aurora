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
//imports the light data
uniform vec4 lightColor;
uniform vec3 lightPos;
uniform vec3 camPos;

void main()
{
	float ambientLight = 0.2f;
	vec3 normal = normalize(Normal);
	vec3 lightDirection = normalize (lightPos - crntPos);
	float diffuse = max(dot(normal, lightDirection), 0.0f);

	float specularLight = 0.5f;
	vec3 viewDirection = normalize(camPos-crntPos);
	vec3 reflectionDirection = reflect(-lightDirection,Normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	FragColor = texture(tex0, texCoord) * (diffuse + ambientLight + specular) * lightColor;
}