#include "Main.h"

#include <glad/glad.h>
#include <GLFW/glfw3.h>
#include <iostream>

#include "Shader.h"
#include "VertexArrayClass.h"
#include "ElementBufferClass.h"

int main()
{
	//initialization, version specification
	glfwInit();
	glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 4);
	glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 4);
	glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);

	//pakuriam taskus kurie bus naudojami trikampiams
	GLfloat vertices[] =
	{
		-0.5f, -0.5f * float(sqrt(3)) / 3, 0.0f,
		0.5f, -0.5f * float(sqrt(3)) / 3, 0.0f,
		0.0f, 0.5f * float(sqrt(3)) * 2 / 3, 0.0f,
		-0.5f / 2, 0.5f * float(sqrt(3)) / 6, 0.0f,
		0.5f / 2, 0.5f * float(sqrt(3)) / 6, 0.0f,
		0.0f, -0.5f * float(sqrt(3)) / 3, 0.0f 
	};
	//pakuriam eile pagal kuria renderinsim taskus
	GLuint indices[] =
	{
		0, 3, 5,
		3, 2, 4,
		5, 4, 1
	};

	//window creation and focusing
	GLFWwindow* window = glfwCreateWindow(800, 800, "Renderer", NULL, NULL);
	if (window == NULL)
	{
		std::cout << "Failed to create GLFW window" << std::endl;
		glfwTerminate();
		return -1;
	}
	glfwMakeContextCurrent(window);

	//an OpenGL configurator. 
	gladLoadGL();
	glViewport(0, 0, 800, 800);				// viewport
	Shader shaderProgram("Default.vert", "Default.frag");
	VertexArrayClass VAO;
	VAO.Bind();
	VertexBufferClass VBO(vertices, sizeof(vertices));
	ElementBufferClass EBO(indices, sizeof(indices));
	VAO.LinkVBO(VBO, 0);
	VAO.Unbind();
	VBO.Unbind();
	EBO.Unbind();


	while (!glfwWindowShouldClose(window))	//when window closes the app closes
	{
		glClearColor(0.07f, 0.13f, 0.17f, 1.0f);// recolor the background. that goes into the buffer
		glClear(GL_COLOR_BUFFER_BIT);			// dump the buffer into live picture
		shaderProgram.Activate();
		VAO.Bind();
		glDrawElements(GL_TRIANGLES, 9, GL_UNSIGNED_INT, 0);
		glfwSwapBuffers(window);				//redraw
		glfwPollEvents();
	}

	//memory cleanup
	VAO.Delete();
	VBO.Delete();
	EBO.Delete();
	shaderProgram.Delete();

	glfwDestroyWindow(window);
	glfwTerminate();
	return 0;
}