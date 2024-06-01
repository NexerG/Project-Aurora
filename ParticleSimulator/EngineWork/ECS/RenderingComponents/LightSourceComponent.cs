using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Model;
using ArctisAurora.EngineWork.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents
{
    internal class LightSourceComponent : EntityComponent
    {
        //the model
        internal Mesh _model;
        internal Vector4 _lightColor = new Vector4(1f, 1f, 1f, 1f);
        internal float _lightIntensity = 1f;
        internal float _attenuationRadius = 5000f;

        //A & E buffers
        internal VAO vao;
        internal VBO vbo;
        internal EBO ebo;
        internal VBO ivbo;

        //instancing
        int instances = 1;
        internal List<Matrix4> instanceMatrix = new List<Matrix4>();

        public LightSourceComponent()
        {
            _model = new Mesh();
            if(_model!=null)
            {
                vao = new VAO();
                vao.Bind();
                vbo = new VBO(_model.vertices);
                ebo = new EBO(_model.indices);

                vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 0);
                vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 3 * sizeof(float));
                vao.LinkAttrib(vbo, 2, 2, VertexAttribPointerType.Float, 11 * sizeof(float), 6 * sizeof(float));
                vao.LinkAttrib(vbo, 3, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 8 * sizeof(float));

                vao.Unbind();
                vbo.Unbind();
                ebo.Unbind();
            }
        }

        public override void OnStart()
        {
            OpenTK_Renderer._rendererInstance.LightToRenderQueue(parent);
            MakeSingleInstance();
        }

        internal void setupUniforms(ShaderClass shader)
        {
            GL.Uniform4(GL.GetUniformLocation(shader.program, "lightColor"), _lightColor.X, _lightColor.Y, _lightColor.Z, _lightColor.W);
        }

        internal void UpdateMatrices(List<Matrix4> IMats)
        {
            if(_model!=null)
            {
                instanceMatrix = IMats;
                vao = new VAO();
                vao.Bind();
                ivbo = new VBO(instanceMatrix);
                vbo = new VBO(_model.vertices);
                ebo = new EBO(_model.indices);

                //initial mesh
                vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 0);
                vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 3 * sizeof(float));
                vao.LinkAttrib(vbo, 2, 2, VertexAttribPointerType.Float, 11 * sizeof(float), 6 * sizeof(float));
                vao.LinkAttrib(vbo, 3, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 8 * sizeof(float));

                //instanced mesh data
                if (instances > 1)
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
        }

        internal void AddMesh(Mesh m)
        {
            _model = m;
        }

        internal void MakeInstanced(int instances, ref List<Matrix4> instanceMatrix)
        {
            this.instances = instances;
            this.instanceMatrix = instanceMatrix;

            UpdateMatrices(this.instanceMatrix);
        }

        internal void MakeSingleInstance()
        {
            Vector3 posTrans = new Vector3(parent.transform.position.X, -parent.transform.position.Y, parent.transform.position.Z);
            Quaternion q = new Quaternion(0.0f, 1.0f, 0.0f, 1.0f);
            Vector3 sc = new Vector3(5.0f, 5.0f, 5.0f);

            Matrix4 translation = Matrix4.Identity;
            Matrix4 rotation = Matrix4.Identity;
            Matrix4 scale = Matrix4.Identity;

            Matrix4.CreateTranslation(posTrans, out translation);
            Matrix4.CreateFromQuaternion(q, out rotation);
            Matrix4.CreateScale(sc, out scale);

            Matrix4 tr = Matrix4.Mult(translation, rotation);
            Matrix4 tr_s = Matrix4.Mult(scale, tr);

            this.instanceMatrix.Add(tr_s);
        }

        public virtual void Draw(ShaderClass shader, Camera camera)
        {
            if (instances == 1)
            {
                Matrix4 mat = instanceMatrix[0];

                //UpdateMatrices(instanceMatrix);
                PreDraw(camera, shader);
                GL.UniformMatrix4(GL.GetUniformLocation(shader.program, "model"), false, ref mat);
                GL.DrawElements(PrimitiveType.Triangles, _model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0);
            }
            else
            {
                UpdateMatrices(instanceMatrix);
                PreDraw(camera, shader);

                GL.DrawElementsInstanced(PrimitiveType.Triangles, _model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0, instances);
            }
        }

        private void PreDraw(Camera camera, ShaderClass shader)
        {
            vao.Bind();
            GL.Uniform3(GL.GetUniformLocation(shader.program, "camPos"), camera.pos.X, camera.pos.Y, camera.pos.Z);
            camera.Matrix(shader, "camMatrix");
        }
    }
}
