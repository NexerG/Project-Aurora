#pragma once

#include<glad/glad.h>
#include"VertexBufferClass.h"

class VertexArrayClass
{
	public:
		// ID reference for the Vertex Array Object
		GLuint ID;
		// Constructor that generates a VAO ID
		VertexArrayClass();

		// Links a VBO to the VAO using a certain layout
		void LinkVBO(VertexBufferClass& VBO, GLuint layout);
		// Binds the VAO
		void Bind();
		// Unbinds the VAO
		void Unbind();
		// Deletes the VAO
		void Delete();
};

