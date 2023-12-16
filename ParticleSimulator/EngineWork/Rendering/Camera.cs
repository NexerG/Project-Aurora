using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ParticleSimulator.EngineWork.Rendering
{
    public class Camera
    {
        //vars init
        public Vector3 pos = new Vector3(-500f, -500f, 350f);

        //directions
        Vector3 Up = new Vector3(0.0f, 1.0f, 0.0f);
        Vector3 Orientation = new Vector3(1.0f, 0.0f, 0.0f);
        Matrix4 pv;

        //controls
        float speed = 0.1f;
        float sensitivity = 10f;
        public Vector2 newpos;
        //public Vector2 oldpos;

        public Camera(Vector3 p)
        {
            //pos = p;
            Orientation.Normalize();
        }

        public void Matrix(ShaderClass shader, string uniform)
        {
            GL.UniformMatrix4(GL.GetUniformLocation(shader.program, uniform), false, ref pv);
        }

        public void updateMatrix(Frame f)
        {
            Matrix4 view = Matrix4.LookAt(pos, pos + Orientation, Up);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60.0f), f.GLControl.AspectRatio, 0.1f, 5000f);
            pv = Matrix4.Mult(view, projection);
        }
        public void rotateCamera(float width, float height)
        {
            float rotY = -sensitivity * (newpos.Y - height) / height;
            float rotX = -sensitivity * (newpos.X - width) / width;

            Quaternion qH = new Quaternion(0f,
                (float)Math.Sin(MathHelper.DegreesToRadians(rotX) / 2),
                0f,
                (float)Math.Cos(MathHelper.DegreesToRadians(rotX) / 2));
            Vector3 newRotH = Vector3.Transform(Orientation, qH);
            newRotH.Normalize();
            Orientation = newRotH;

            Quaternion qV = new Quaternion((float)Math.Sin(MathHelper.DegreesToRadians(rotY) / 2),
                0f,
                0f,
                (float)Math.Cos(MathHelper.DegreesToRadians(rotY) / 2));
            Vector3 newRotV = Vector3.Transform(Orientation, qV);
            newRotV.Normalize();
            Orientation = newRotV;
        }
        public void moveCamera(KeyPressEventArgs e)
        {
            //handles movement
            if (e.KeyChar == 'w')
            {
                //forward
                pos += speed * Orientation;
            }
            if (e.KeyChar == 'a')
            {
                //left
                pos += speed * -Vector3.Normalize(Vector3.Cross(Orientation, Up));
            }
            if (e.KeyChar == 'd')
            {
                //right
                pos += speed * Vector3.Normalize(Vector3.Cross(Orientation, Up));
            }
            if (e.KeyChar == 's')
            {
                //back
                pos += speed * -Orientation;
            }
            if (e.KeyChar == (char)System.Windows.Forms.Keys.Space)
            {
                //up
                pos += speed * Up;
            }
            if (e.KeyChar == 'e')
            {
                //up
                pos += speed * Up;
            }
            if (e.KeyChar == 'q')
            {
                //down
                pos += speed * -Up;
            }
        }
    }
}