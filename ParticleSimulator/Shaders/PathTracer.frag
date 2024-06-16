#version 430 core

out vec4 FragColor;

void main()
{
	vec2 pixelCoords = gl_FragCoord.xy;

	FragColor = vec4(pixelCoords.x/1280, 0, pixelCoords.y/720, 1.0);
}