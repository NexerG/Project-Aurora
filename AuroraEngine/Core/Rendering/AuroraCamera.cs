using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.Modules;
using System.Numerics;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using Keys = Silk.NET.GLFW.Keys;

using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UI;
using ArctisAurora.Core.UISystem.Controls;

namespace ArctisAurora.EngineWork.Rendering
{   
    internal class AuroraCamera
    {
        //camera buffer
        internal Buffer[] _cameraBuffer;
        internal DeviceMemory[] _camBmemory;
        internal nint[] _cameraMapped;
        //keyboard
        internal Dictionary<Keys, bool> _keyStates = new Dictionary<Keys, bool>();
        //variables
        internal Vector3 _pos = new Vector3(0, 0, 0);
        internal Vector3 _rotation = new Vector3(0, 0, 0);
        internal Vector3 _localUp = new Vector3(0, 1, 0);
        internal Vector3 _front = new Vector3(0, 0, -1);
        internal Vector3 _localRight = new Vector3(0, 0, 0);
        //matrices
        internal Matrix4x4 _view = Matrix4x4.Identity;
        internal Matrix4x4 _projection = Matrix4x4.Identity;
        //controls
        float _speed = 0.5f;
        float _sensitivity = 0.25f;
        //control vars
        private bool _firstMove = true;
        private double _lastX, _lastY;

        // the module this camera belongs to — it names the projection and the window
        private readonly RenderingModule _owner;

        internal AuroraCamera(RenderingModule owner) : this(owner, owner.window.imageCount) { }

        // The dead renderer types (Rasterizer, Pathtracing, RadianceCascades2D, UIRenderer) build a
        // camera with no module behind it; three images is what they always assumed.
        internal AuroraCamera() : this(null, 3) { }

        private AuroraCamera(RenderingModule owner, uint imageCount)
        {
            _owner = owner;
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                _keyStates[key] = false;
            }

            ulong bufferSize = (ulong)Unsafe.SizeOf<UBO>();
            _cameraBuffer = new Buffer[imageCount];
            _camBmemory = new DeviceMemory[imageCount];
            _cameraMapped = new nint[imageCount];
            for (int i = 0; i < imageCount; i++)
            {
                AVulkanBufferHandler.CreateMappedBuffer(bufferSize, ref _cameraBuffer[i], ref _camBmemory[i], out _cameraMapped[i], BufferUsageFlags.UniformBufferBit);
            }
        }

        internal void UpdateCameraMatrix(Extent2D _extent, uint currentImage)
        {

            switch (_owner.rendererType)
            {
                case ERendererTypes.Rasterizer:
                    _front.X = MathF.Cos(float.DegreesToRadians(_rotation.X)) * MathF.Cos(float.DegreesToRadians(_rotation.Y));
                    _front.Y = MathF.Sin(float.DegreesToRadians(_rotation.Y));
                    _front.Z = MathF.Sin(float.DegreesToRadians(_rotation.X)) * MathF.Cos(float.DegreesToRadians(_rotation.Y));
                    _front = Vector3.Normalize(_front);

                    _localRight = Vector3.Normalize(Vector3.Cross(_front, Vector3.UnitY));
                    _localUp = Vector3.Normalize(Vector3.Cross(_localRight, _front));

                    _view = Matrix4x4.CreateLookAt(_pos, _pos + _front, Vector3.UnitY);
                    _projection = Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(60.0f), _extent.Width / _extent.Height, 0.1f, 512f);
                    _projection.M22 *= -1;
                    break;

                case ERendererTypes.Pathtracer:
                    Matrix4x4 _tempView;
                    Matrix4x4 _tempProjection;

                    Matrix4x4.Invert(_view, out _tempView);
                    Matrix4x4.Invert(_projection, out _tempProjection);
                    _view = _tempView;
                    _projection = _tempProjection;
                    break;

                case ERendererTypes.UITemp:
                    UIModule ui = (UIModule)_owner;
                    Vector2 box;
                    Vector2 origin = Vector2.Zero;

                    if (ui.rangeRoot != null)
                    {
                        // a drag preview: the control's own box, so it fills the window at any extent
                        box = ui.rangeRoot.arrangedRect.size;
                        origin = new Vector2(ui.rangeRoot.arrangedRect.x, ui.rangeRoot.arrangedRect.y);
                    }
                    else
                    {
                        WindowControl root = ui.uiRoot;
                        box = root != null
                            ? root.ViewportSize(_extent)
                            : new Vector2(_extent.Width, _extent.Height);
                    }

                    _view = Matrix4x4.CreateLookAt(Vector3.Zero, _front, _localUp);
                    _projection = Matrix4x4.CreateOrthographicOffCenter(origin.X, origin.X + box.X,
                        origin.Y, origin.Y + box.Y, 0.01f, 512f);
                    break;

                case ERendererTypes.UIEngine:
                    UIEngineModule nextModule = (UIEngineModule)_owner;
                    LayoutRect? ghost = nextModule.rangeRect;
                    WindowRoot nextRoot = nextModule.uiRoot;
                    Vector2 nextOrigin = Vector2.Zero;
                    Vector2 nextBox;

                    if (ghost.HasValue)
                    {
                        // a drag preview: the control's own box, so it fills the window at any extent
                        nextBox = ghost.Value.size;
                        nextOrigin = new Vector2(ghost.Value.x, ghost.Value.y);
                    }
                    else
                        nextBox = nextRoot != null
                            ? nextRoot.ViewportSize(_extent)
                            : new Vector2(_extent.Width, _extent.Height);

                    _view = Matrix4x4.CreateLookAt(Vector3.Zero, _front, _localUp);
                    _projection = Matrix4x4.CreateOrthographicOffCenter(nextOrigin.X, nextOrigin.X + nextBox.X,
                        nextOrigin.Y, nextOrigin.Y + nextBox.Y, 0.01f, 512f);
                    break;
                default:
                    break;
            }

            UBO _ubo = new UBO()
            {
                _view = _view,
                _projection = _projection,
                //_lightProjection = Rasterizer._lightsToRender[0].GetComponent<LightsourceComponent>()._lightProjection,
                //_lightView = Rasterizer._lightsToRender[0].GetComponent<LightsourceComponent>()._lightView,
                //_camPos = _camera._pos
            };

            unsafe { Unsafe.Write((void*)_cameraMapped[currentImage], _ubo); }
        }

        internal unsafe void Destroy()
        {
            for (int i = 0; i < _cameraBuffer.Length; i++)
            {
                Renderer.vk.UnmapMemory(Renderer.logicalDevice, _camBmemory[i]);
                Renderer.vk.DestroyBuffer(Renderer.logicalDevice, _cameraBuffer[i], null);
                Renderer.vk.FreeMemory(Renderer.logicalDevice, _camBmemory[i], null);
            }
        }

        internal void ProcessMouseMovements(double xPos, double yPos, bool _constrainPitch = true)
        {
            if (_owner.rendererType == ERendererTypes.UITemp)
            {
                return;
            }
            if (_firstMove)
            {
                _lastX = xPos;
                _lastY = yPos;
                _firstMove = false;
            }

            Vector2 _delta = new Vector2((float)(xPos - _lastX), (float)(yPos - _lastY));
            _lastX = xPos;
            _lastY = yPos;

            _delta *= _sensitivity;

            _rotation.X += _delta.X;
            _rotation.Y -= _delta.Y;

            if (_constrainPitch)
            {
                _rotation.Y = Clamp(_rotation.Y, -89.0f, 89.0f);
            }
        }

        internal void ProcessKeyboard()
        {
            //WASD just wasd man
            if (_keyStates[Keys.W])
            {
                _pos += _speed * _front;
            }
            if (_keyStates[Keys.A])
            {
                _pos += _speed * -_localRight;
            }
            if (_keyStates[Keys.D])
            {
                _pos += _speed * _localRight;
            }
            if (_keyStates[Keys.S])
            {
                _pos += _speed * -_front;
            }
            //EQ up down on unitY
            if (_keyStates[Keys.E])
            {
                _pos += _speed * Vector3.UnitY;
            }
            if (_keyStates[Keys.Q])
            {
                _pos += _speed * -Vector3.UnitY;
            }
            //space ctrl local up down
            if (_keyStates[Keys.LeftControl])
            {
                _pos += _speed * -_localUp;
            }
            if (_keyStates[Keys.Space])
            {
                _pos += _speed * _localUp;
            }
        }

        private float Clamp(float toClamp, float bottom, float top)
        {
            if (toClamp < bottom)
                return bottom;
            else if (toClamp > top)
                return top;
            else
                return toClamp;
        }

        internal static Vector2 GetPixelSizeInWorldSpace(float left, float right, float bot, float top, int screenWidth, int screenHeight)
        {
            float worldWidth = right - left;
            float worldHeight = top - bot;

            float pixelWidth = worldWidth / screenWidth;
            float pixelHeight = worldHeight / screenHeight;

            return new Vector2(pixelWidth, pixelHeight);
        }

        internal void UpdateCameraMatrix(Extent2D windowExtent, uint imageIndex, int i)
        {
            throw new NotImplementedException();
        }
    }
}