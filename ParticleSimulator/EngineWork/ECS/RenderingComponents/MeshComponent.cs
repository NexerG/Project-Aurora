using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ArctisAurora.EngineWork.ComponentBehaviour;
using ArctisAurora.EngineWork.Model;
using ArctisAurora.EngineWork.Rendering;
using static ArctisAurora.EngineWork.Rendering.ShaderClass;
using Assimp;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;

namespace ArctisAurora.EngineWork.ECS.RenderingComponents
{
    internal class MeshComponent : EntityComponent
    {
        //variable
        bool render = true;
        //the model
        internal Model.Mesh model;
        //A & E buffers
        public VAO vao;
        public VBO vbo;
        public EBO ebo;
        public VBO ivbo;

        //instancing
        int instances = 1;
        internal List<Matrix4> instanceMatrix = new List<Matrix4>();
        entityShaderType type = entityShaderType.entity;

        public MeshComponent()
        {
            model = new Model.Mesh();
        }

        internal void LoadCustomMesh(Scene sc)
        {
            model.LoadCustomMesh(sc);
        }

        public override void OnStart()
        {
            OpenTK_Renderer._rendererInstance.EntityToRenderQueue(parent);
            MakeSingleInstance();
        }

        public override void OnDisable()
        {
            render = false;
        }

        public override void OnEnable()
        {
            render = true;
        }

        internal void UpdateMatrices()
        {
            vao = new VAO();
            vao.Bind();
            ivbo = new VBO(instanceMatrix);
            vbo = new VBO(model.vertices);
            ebo = new EBO(model.indices);

            //initial mesh
            vao.LinkAttrib(vbo, 0, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 0);
            //vao.LinkAttrib(vbo, 1, 3, VertexAttribPointerType.Float, 11 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 1, 2, VertexAttribPointerType.Float, 8 * sizeof(float), 3 * sizeof(float));
            vao.LinkAttrib(vbo, 2, 3, VertexAttribPointerType.Float, 8 * sizeof(float), 6 * sizeof(float));

            //instanced mesh data
            //if(instances > 1)
            {
                ivbo.Bind();
                vao.LinkAttrib(ivbo, 3, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 0);
                vao.LinkAttrib(ivbo, 4, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 1 * 16);
                vao.LinkAttrib(ivbo, 5, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 2 * 16);
                vao.LinkAttrib(ivbo, 6, 4, VertexAttribPointerType.Float, 16 * sizeof(float), 3 * 16);
                GL.VertexAttribDivisor(3, 1);
                GL.VertexAttribDivisor(4, 1);
                GL.VertexAttribDivisor(5, 1);
                GL.VertexAttribDivisor(6, 1);
            }

            vao.Unbind();
            //vbo.Unbind();
            ivbo.Unbind();
            //ebo.Unbind();
        }

        internal void MakeInstanced(int instances, ref List<Matrix4> instanceMatrix)
        {
            this.instances = instances;
            this.instanceMatrix = instanceMatrix;

            UpdateMatrices();
        }

        internal void MakeSingleInstance()
        {
            Vector3 pos = new Vector3(parent.transform.position.X, parent.transform.position.Y, parent.transform.position.Z);
            OpenTK.Mathematics.Quaternion q = OpenTK.Mathematics.Quaternion.FromEulerAngles(parent.transform.rotation);

            Matrix4 transformation = Matrix4.Identity;
            transformation *= Matrix4.CreateTranslation(pos);
            transformation *= Matrix4.CreateFromQuaternion(q);
            transformation *= Matrix4.CreateScale(parent.transform.scale);

            this.instanceMatrix.Add(transformation);
            UpdateMatrices();
        }

        public void Draw(ShaderClass shader)
        {
            if(render)
            {
                UpdateMatrices();
                PreDraw(shader);
                if (instances == 1)
                {
                    GL.DrawElements(PrimitiveType.Triangles, model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0);
                }
                else
                {
                    GL.DrawElementsInstanced(PrimitiveType.Triangles, model.indices.Length * sizeof(uint) / sizeof(int), DrawElementsType.UnsignedInt, 0, instances);
                }
            }
        }

        private void PreDraw(ShaderClass shader)
        {
            vao.Bind();
            model.textures.texUnit(shader, "tex0", 0);
            model.textures.Bind();
        }
    }
}