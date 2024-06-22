using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace ArctisAurora.EngineWork.Rendering.Renderers.Renderer_Vulkan
{
    internal class AVulkanCamera
    {
        //variables
        internal Vector3D<float> _pos = new Vector3D<float>(2, 2, 2);
        internal Vector3D<float> _rotation = new Vector3D<float>(0, 0, 0);
        internal Vector3D<float> _localUp = new Vector3D<float>(0, 1, 0);
        internal Vector3D<float> _front = new Vector3D<float>(0, 0, 1);
        internal Vector3D<float> _localRight = new Vector3D<float> (0, 0, 0);
        //matrices
        internal Matrix4X4<float> _view;
        internal Matrix4X4<float> _projection;
        internal Matrix4X4<float> _pv;
        //controls
        float _speed = 0.001f;
        float _sensitivity = 0.25f;

        internal void UpdateCameraMatrix(Extent2D _extent)
        {
            _front.X = MathF.Cos(Scalar.DegreesToRadians(_rotation.X)) * MathF.Cos(Scalar.DegreesToRadians(_rotation.Y));
            _front.Y = MathF.Sin(Scalar.DegreesToRadians(_rotation.Y));
            _front.Z = MathF.Sin(Scalar.DegreesToRadians(_rotation.X)) * MathF.Cos(Scalar.DegreesToRadians(_rotation.Y));
            _front = Vector3D.Normalize(_front);

            _localRight = Vector3D.Normalize(Vector3D.Cross(_front,new Vector3D<float>(0,1,0)));
            _localUp = Vector3D.Normalize(Vector3D.Cross(_localRight, _front));

            _view = Matrix4X4.CreateLookAt(_pos, new Vector3D<float>(0,0,0), new Vector3D<float>(0,1,0));
            _projection = Matrix4X4.CreatePerspectiveFieldOfView(Scalar.DegreesToRadians(45.0f), _extent.Width / _extent.Height, 0.1f, 10f);

            _pv = _view * _projection;
        }

        internal void ProcessMouseMovements()
        {

        }

        internal void ProcessKeyboard()
        {

        }
    }
}
