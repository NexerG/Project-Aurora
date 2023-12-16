using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ParticleSimulator.EngineWork.Rendering;
using ParticleSimulator.ParticleTypes;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.EngineWork.Model
{
    public class Mesh
    {
        float[] vertices;
        uint[] indices;
        Texture textures;
        int instancing;
        public List<Matrix4> instanceMatrix;
        //A & E buffer
        public VAO vao;
        public VBO vbo;
        public EBO ebo;
        public VBO ivbo;

        public Mesh(float[] vertices, uint[] indices, int instancing, Texture texture, ref List<Matrix4> instanceMatrix)
        {
            this.vertices = vertices;
            this.indices = indices;
            this.textures = texture;
            this.instancing = instancing;
            this.instanceMatrix = instanceMatrix;

            vao = new VAO();
            vao.Bind();
            ivbo = new VBO(instanceMatrix);
            vbo = new VBO(vertices);
            ebo = new EBO(indices);

            vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
            vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 2, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 6 * sizeof(float));
            //vao.LinkAttrib(vbo, 3, 2, VertexAttribPointerType.Float, 11 * sizeof(float), 9 * sizeof(float));
            if (instancing != 1)
            {
                ivbo.Bind();
                vao.LinkAttrib(ivbo, 4, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 0);
                vao.LinkAttrib(ivbo, 5, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 1 * 16);
                vao.LinkAttrib(ivbo, 6, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 2 * 16);
                vao.LinkAttrib(ivbo, 7, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 3 * 16);
                GL.VertexAttribDivisor(4, 1);
                GL.VertexAttribDivisor(5, 1);
                GL.VertexAttribDivisor(6, 1);
                GL.VertexAttribDivisor(7, 1);
            }
            vao.Unbind();
            vbo.Unbind();
            ivbo.Unbind();
            ebo.Unbind();
        }
        public void updateMatrices(List<Matrix4> IMats)
        {
            instanceMatrix = IMats;
            vao = new VAO();
            vao.Bind();
            ivbo = new VBO(instanceMatrix);
            vbo = new VBO(vertices);
            ebo = new EBO(indices);

            vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
            vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 2, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 6 * sizeof(float));
            vao.LinkAttrib(vbo, 3, 2, VertexAttribPointerType.Float, 11 * sizeof(float), 9 * sizeof(float));
            if (instancing != 1)
            {
                ivbo.Bind();
                vao.LinkAttrib(ivbo, 4, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 0);
                vao.LinkAttrib(ivbo, 5, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 1 * 16);
                vao.LinkAttrib(ivbo, 6, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 2 * 16);
                vao.LinkAttrib(ivbo, 7, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 3 * 16);
                GL.VertexAttribDivisor(4, 1);
                GL.VertexAttribDivisor(5, 1);
                GL.VertexAttribDivisor(6, 1);
                GL.VertexAttribDivisor(7, 1);
            }
            vao.Unbind();
            vbo.Unbind();
            ivbo.Unbind();
            ebo.Unbind();
        }
        public void Draw(ShaderClass shader, Camera camera, Matrix4 mat, Vector3 translation, Quaternion rotation, Vector3 scale)
        {
            //shader.Activate();
            vao.Bind();
            textures.texUnit(shader, "tex0", 0);
            textures.Bind();

            GL.Uniform3(GL.GetUniformLocation(shader.program, "camPos"), camera.pos.X, camera.pos.Y, camera.pos.Z);
            camera.Matrix(shader, "camMatrix");
            if (instancing == 1)
            {
                //Matrix4 trans, rot, sca;
                //Matrix4.CreateTranslation(translation, out trans);
                //Matrix4.CreateFromQuaternion(rotation, out rot);
                //Matrix4.CreateScale(scale, out sca);
                //GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "translation"), false, ref trans);
                //GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "rotation"), false, ref rot);
                //GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "scale"), false, ref sca);
                GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "model"), false, ref mat);
                GL.DrawElements(PrimitiveType.Triangles, indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0);
            }
            else
            {
                GL.DrawElementsInstanced(PrimitiveType.Triangles, indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0, instancing);
            }
        }
    }
}
