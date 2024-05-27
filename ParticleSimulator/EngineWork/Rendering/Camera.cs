using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArctisAurora.EngineWork.Rendering
{
    public class Camera
    {
        //vars init
        public Vector3 pos = new Vector3(-500f, -500f, 350f);

        //directions
        float pitch=0;
        float yaw=0;
        float roll=0;
        Matrix4 pv;

        //controls
        float speed = 0.1f;
        float sensitivity = .25f;

        public Camera()
        {

        }

        public void Matrix(ShaderClass shader, string uniform)
        {
            GL.UniformMatrix4(GL.GetUniformLocation(shader.program, uniform), false, ref pv);
        }

        public void updateMatrix()
        {
            Vector3 front;
            front.X = MathF.Cos(MathHelper.DegreesToRadians(yaw)) * MathF.Cos(MathHelper.DegreesToRadians(pitch));
            front.Y = MathF.Sin(MathHelper.DegreesToRadians(pitch));
            front.Z = MathF.Sin(MathHelper.DegreesToRadians(yaw)) * MathF.Cos(MathHelper.DegreesToRadians(pitch));
            front = Vector3.Normalize(front);

            Vector3 right = Vector3.Normalize(Vector3.Cross(front, Vector3.UnitY));
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, front));

            Matrix4 view = Matrix4.LookAt(pos, pos + front, up);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), 1920 / 1080, 0.1f, 5000f);
            pv = view * projection;
        }

        internal void ProcessMouseMovement(Vector2 delta, bool constrainPitch = true)
        {
            delta *= sensitivity;

            yaw += delta.X;
            pitch -= delta.Y;

            if (constrainPitch)
            {
                pitch = MathHelper.Clamp(pitch, -89.0f,89.0f);
            }
            //Console.WriteLine("Orientation " + yaw + "  " + pitch);
        }
    }
}