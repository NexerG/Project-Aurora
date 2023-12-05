#include "VertexArrayClass.h"

VertexArrayClass::VertexArrayClass()
{
	glGenVertexArrays(1, &ID);
}

// Links a VBO to the VAO using a certain layout
void VertexArrayClass::LinkVBO(VertexBufferClass& VBO, GLuint layout)
{
	VBO.Bind();
	glVertexAttribPointer(layout, 3, GL_FLOAT, GL_FALSE, 0, (void*)0);
	glEnableVertexAttribArray(layout);
	VBO.Unbind();
}

// Binds the VAO
void VertexArrayClass::Bind()
{
	glBindVertexArray(ID);
}

// Unbinds the VAO
void VertexArrayClass::Unbind()
{
	glBindVertexArray(0);
}

// Deletes the VAO
void VertexArrayClass::Delete()
{
	glDeleteVertexArrays(1, &ID);
}