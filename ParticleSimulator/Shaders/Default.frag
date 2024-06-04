#version 430

// Outputs colors in RGBA
out vec4 FragColor;

// Inputs the texture coordinates from the Vertex Shader
in vec2 texCoord;
in vec3 Normal;
in vec3 crntPos;

layout(std430, binding = 0) buffer DebugBuffer
{
	vec3 test;
};


// Textures
uniform sampler2D tex0;
uniform sampler2D tex1;
//imports the light data
uniform vec4 lightColor;
uniform vec3 lightPos;
//for the perspective
uniform vec3 camPos;

vec4 pointLight(vec3 lightPosition)
{
	//intensity
	vec3 lightVec = lightPosition - crntPos;
	float dist = length(lightVec);
	float a = 3.0f;
	float b = 0.7f;
	float intensity = (1.0f /(a * dist * dist + b * dist + 1.0f)) * 3000000.0f;
	
	float ambientLight = 0.20f;

	vec3 lightDirection = normalize(lightVec);
	float diffuse = max(dot(Normal, lightDirection), 0.0f);

	float specular = 0.0f;
	if(diffuse != 0.0f)
	{
		float specularLight = 0.50f;
		vec3 viewDirection = normalize(camPos - crntPos);
		vec3 halfWayVec = normalize(viewDirection + lightDirection);
		float specAmount = pow(max(dot(Normal, halfWayVec), 0.0f), 16);
		specular = specAmount * specularLight;
	}
	return (texture(tex0, texCoord) * (diffuse * intensity + ambientLight)  + texture(tex1, texCoord).r * specular * intensity) * lightColor;
}

vec4 directLight()
{
	float ambientLight  = 0.5f;

	//diffuse
	vec3 lightDirection = normalize(vec3(1.0f, 1.0f, 0.0f));
	float diffuse = max(dot(Normal, lightDirection), 0.0f);

	//specular
	float specularLight = 0.5f;
	vec3 viewDirection = normalize(camPos - crntPos);
	vec3 reflectionDirection = reflect(-lightDirection, Normal);
	float specAmount = pow(max(dot(viewDirection, reflectionDirection),0),8);
	float specular = specAmount * specularLight;

	return ((diffuse + ambientLight) * texture(tex0, texCoord) + specular * texture(tex1, texCoord).r) * lightColor;
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
	float diffuse = max(dot(Normal, lightDirection), 0.0f);

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
	//test = camPos;
	/*vec4 accumulativeLight = vec4(0.0);
	for(int i = 0; i < 1; ++i)
	{
		accumulativeLight += pointLight(lightPos[i]);
	}*/
	//FragColor = pointLight(lightPos);

    float ambientStrength = 0.1;
    vec3 ambient = ambientStrength * lightColor.xyz;

    //We calculate the light direction, and make sure the normal is normalized.
    vec3 norm = normalize(Normal);
    vec3 lightDir = normalize(lightPos - crntPos); //Note: The light is pointing from the light to the fragment

    //The diffuse part of the phong model.
    //This is the part of the light that gives the most, it is the color of the object where it is hit by light.
    float diff = max(dot(norm, lightDir), 0.0); //We make sure the value is non negative with the max function.
    vec3 diffuse = diff * lightColor.xyz;


    //The specular light is the light that shines from the object, like light hitting metal.
    //The calculations are explained much more detailed in the web version of the tutorials.
    float specularStrength = 0.5;
    vec3 viewDir = normalize(camPos - crntPos);
    vec3 reflectDir = reflect(-lightDir, norm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32); //The 32 is the shininess of the material.
    vec3 specular = specularStrength * spec * lightColor.xyz;

    //At last we add all the light components together and multiply with the color of the object. Then we set the color
    //and makes sure the alpha value is 1
    vec3 result = (ambient + diffuse + specular) * texture(tex0, texCoord).xyz;
    FragColor = vec4(result, 1.0);
    }