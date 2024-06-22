using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Model;
using static ArctisAurora.EngineWork.Rendering.Renderers.OpenTK.ShaderClass;
using Assimp;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;
using ArctisAurora.EngineWork.Rendering.Renderers.OpenTK;
using Quaternion = OpenTK.Mathematics.Quaternion;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents.OpenTK
{
    internal class MeshComponent_OpenTK : EntityComponent
    {
        //variable
        bool render = true;
        //the model
        internal Model.Mesh model;

        //A & E buffers
        public VAO vao;
        public VBO vbo = new VBO();
        public VBO ivbo = new VBO();
        public EBO ebo = new EBO();

        //instancing
        int instances = 1;
        internal List<Matrix4> instanceMatrix = new List<Matrix4>();
        entityShaderType type = entityShaderType.entity;

        public MeshComponent_OpenTK()
        {
            instanceMatrix.Add(Matrix4.Identity);
            model = new Model.Mesh();
            if (model != null)
            {
                vao = new VAO();
                vao.Bind();

                ebo.BufferElementData(model.indices);
                vbo.BufferVertexData(model.vertices);

                //tell the gpu what vertex buffer part is what
                vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
                vao.LinkAttrib(vbo, 1, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
                vao.LinkAttrib(vbo, 2, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 5 * sizeof(float));
                //instance matrix attributes
                vao.LinkAttrib(ivbo, 3, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 0);
                vao.LinkAttrib(ivbo, 4, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 1 * 16);
                vao.LinkAttrib(ivbo, 5, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 2 * 16);
                vao.LinkAttrib(ivbo, 6, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 3 * 16);
                GL.VertexAttribDivisor(3, 1);
                GL.VertexAttribDivisor(4, 1);
                GL.VertexAttribDivisor(5, 1);
                GL.VertexAttribDivisor(6, 1);

                vao.Unbind();
            }
        }

        internal void LoadCustomMesh(Scene sc)
        {
            model.LoadCustomMesh(sc);

            vao.Bind();

            vbo.BufferVertexData(model.vertices);
            ebo.BufferElementData(model.indices);

            vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
            vao.LinkAttrib(vbo, 1, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 2, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 5 * sizeof(float));

            vao.Unbind();
        }

        public override void OnStart()
        {
            Rasterization._rendererInstance.EntityToRenderQueue(parent);
        }

        public override void OnDisable()
        {
            render = false;
        }

        public override void OnEnable()
        {
            render = true;
        }

        private void BufferInstances()
        {
            ivbo.BufferMatrixData(instanceMatrix);
        }

        internal void MakeInstanced(int instances, ref List<Matrix4> instanceMatrix)
        {
            this.instances = instances;
            this.instanceMatrix = instanceMatrix;
        }

        internal void SingletonMatrix()
        {
            Vector3 pos = new Vector3(parent.transform.position.X, parent.transform.position.Y, parent.transform.position.Z);
            Quaternion q = Quaternion.FromEulerAngles(parent.transform.rotation);

            Matrix4 transformation = Matrix4.Identity;
            transformation *= Matrix4.CreateScale(parent.transform.scale);
            transformation *= Matrix4.CreateFromQuaternion(q);
            transformation *= Matrix4.CreateTranslation(pos);

            instanceMatrix[0] = transformation;

            BufferInstances();
        }

        public void Draw(ShaderClass shader)
        {
            if (render)
            {
                vao.Bind();
                PreDraw(shader);
                if (instances == 1)
                {
                    SingletonMatrix();
                    GL.DrawElements(PrimitiveType.Triangles, model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0);
                }
                else
                {
                    BufferInstances();
                    GL.DrawElementsInstanced(PrimitiveType.Triangles, model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0, instances);
                }
                vao.Unbind();
            }
        }

        private void PreDraw(ShaderClass shader)
        {
            model.textures.texUnit(shader, "tex0", 0);
            model.textures.Bind();
        }
    }
}