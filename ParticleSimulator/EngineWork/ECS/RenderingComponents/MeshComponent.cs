using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ParticleSimulator.EngineWork.ComponentBehaviour;
using ParticleSimulator.EngineWork.Model;
using ParticleSimulator.EngineWork.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParticleSimulator.EngineWork.ECS.RenderingComponents
{
    internal class MeshComponent : EntityComponent
    {
        internal Mesh model;
        internal ShaderClass shader = new ShaderClass();

        //A & E buffers
        public VAO vao;
        public VBO vbo;
        public EBO ebo;
        public VBO ivbo;

        //instancing
        int instances = 1;
        internal List<Matrix4> instanceMatrix;

        public MeshComponent()
        {
            model = new Mesh();

            vao = new VAO();
            vao.Bind();
            vbo = new VBO(model.vertices);
            ebo = new EBO(model.indices);

            vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
            vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 2, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 6 * sizeof(float));
            //vao.LinkAttrib(vbo, 3, 2, VertexAttribPointerType.Float, 11 * sizeof(float), 9 * sizeof(float));

            vao.Unbind();
            vbo.Unbind();
            ebo.Unbind();
        }

        public override void OnStart()
        {
            OpenTK_Renderer._rendererInstance._renderQueue.Add(parent);
        }

        internal void UpdateMatrices(List<Matrix4> IMats)
        {
            //instanceMatrix = IMats;
            vao = new VAO();
            vao.Bind();
            ivbo = new VBO(instanceMatrix);
            vbo = new VBO(model.vertices);
            ebo = new EBO(model.indices);

            //initial mesh
            vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
            vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 2, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 6 * sizeof(float));
            vao.LinkAttrib(vbo, 3, 2, VertexAttribPointerType.Float, 11 * sizeof(float), 9 * sizeof(float));

            //instanced mesh data
            ivbo.Bind();
            vao.LinkAttrib(ivbo, 4, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 0);
            vao.LinkAttrib(ivbo, 5, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 1 * 16);
            vao.LinkAttrib(ivbo, 6, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 2 * 16);
            vao.LinkAttrib(ivbo, 7, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 3 * 16);
            GL.VertexAttribDivisor(4, 1);
            GL.VertexAttribDivisor(5, 1);
            GL.VertexAttribDivisor(6, 1);
            GL.VertexAttribDivisor(7, 1);

            vao.Unbind();
            vbo.Unbind();
            ivbo.Unbind();
            ebo.Unbind();
        }

        internal void AddMesh(Mesh m)
        {
            model = m;
        }

        internal void MakeInstanced(int instances, ref List<Matrix4> instanceMatrix)
        {
            this.instances = instances;
            this.instanceMatrix = instanceMatrix;

            UpdateMatrices(this.instanceMatrix);
        }

        public void Draw(ShaderClass shader, Camera camera, Matrix4 mat)
        {
            //shader.Activate();
            if (instances == 1)
            {
                PreDraw(camera);

                //Matrix4 trans, rot, sca;
                //Matrix4.CreateTranslation(translation, out trans);
                //Matrix4.CreateFromQuaternion(rotation, out rot);
                //Matrix4.CreateScale(scale, out sca);
                //GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "translation"), false, ref trans);
                //GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "rotation"), false, ref rot);
                //GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "scale"), false, ref sca);
                GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "model"), false, ref mat);
                GL.DrawElements(PrimitiveType.Triangles, model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0);
            }
            else
            {
                Console.WriteLine("tipo piesiam");
                UpdateMatrices(instanceMatrix);
                PreDraw(camera);

                GL.DrawElementsInstanced(PrimitiveType.Triangles, model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0, instances);
            }
        }

        private void PreDraw(Camera camera)
        {
            vao.Bind();
            model.textures.texUnit(shader, "tex0", 0);
            model.textures.Bind();

            GL.Uniform3(GL.GetUniformLocation(shader.program, "camPos"), camera.pos.X, camera.pos.Y, camera.pos.Z);
            camera.Matrix(shader, "camMatrix");

        }
    }
}
