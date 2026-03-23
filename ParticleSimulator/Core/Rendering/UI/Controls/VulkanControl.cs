using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Physics.UICollision;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using ArctisAurora.EngineWork.Serialization;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System;
using System.CodeDom;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using static ArctisAurora.EngineWork.Rendering.UI.Controls.VulkanControl;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    public interface IXMLChild_UI
    { }

    public unsafe class VulkanControl : Entity, IXMLParser<VulkanControl>, IXMLChild_UI
    {
        #region STRUCTS
        [StructLayout(LayoutKind.Sequential, Pack = 1), A_XSDType("ControlStyle", "Registry")]
        public struct ControlStyle
        {
            public Vector3D<float> tint;
            //public Sampler image;
            //public Sampler mask;

            public static ControlStyle Default()
            {
                Dictionary<string, ControlStyle> dStyles = AssetRegistries.GetRegistryByValueType<string, ControlStyle>(typeof(ControlStyle));
                return dStyles.GetValueOrDefault("default");
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct QuadUVs
        {
            public Vector2D<float> uv1;
            public Vector2D<float> uv2;
            public Vector2D<float> uv3;
            public Vector2D<float> uv4;

            public QuadUVs(Vector2D<float> uv1, Vector2D<float> uv2, Vector2D<float> uv3, Vector2D<float> uv4)
            {
                this.uv1 = uv1;
                this.uv2 = uv2;
                this.uv3 = uv3;
                this.uv4 = uv4;
            }

            public QuadUVs(Vector2D<float>[] uvs)
            {
                uv1 = uvs[0];
                uv2 = uvs[1];
                uv3 = uvs[2];
                uv4 = uvs[3];
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ControlData()
        {
            public QuadUVs uvs;
            public ControlStyle style;
        }

        [A_XSDType("ControlColor", "UI")]
        public enum ControlColor
        {
            red, green, blue, white, black, yellow, cyan, magenta, gray, orange, purple, brown, pink, lime, navy, teal,
        }

        [A_XSDType("FillMode", "UI")]
        public enum ScalingMode
        {
            Uniform,
            Stretch,
            Fill,
            None
        }

        /*[A_VulkanEnum("ScaleMode")]
        public enum ScaleMode
        {
            PrioritizeWidth,
            PrioritizeHeight,
            None
        }*/
        #endregion

        #region UI XML fields

        #region scaling
        private int _width = 72;
        private int _height = 72;
        public int width
        {
            get => _width;
            set
            {
                _width = value;
                transform.SetWorldScale(new Vector3D<float>(width, height, 1));
            }
        }
        public int height
        {
            get => _height;
            set
            {
                _height = value;
                transform.SetWorldScale(new Vector3D<float>(width, height, 1));
            }
        }


        [A_XSDElementProperty("Width", "UI", "Width in pixels.")]
        public int preferredWidth = 72;
        [A_XSDElementProperty("Height", "UI", "Height in pixels.")]
        public int preferredHeight = 72;

        [A_XSDElementProperty("MinHeight", "UI", "Minimum height in pixels.")]
        public int minHeight = 0;
        [A_XSDElementProperty("MinWidth", "UI", "Minimum width in pixels.")]
        public int minWidth = 0;

        public virtual Vector2D<int> size
        {
            get => new Vector2D<int>(_width, _height);
            set
            {
                _width = value.X;
                _height = value.Y;
                transform.SetWorldScale(new Vector3D<float>(width, height, 1));
            }
        }
        #endregion

        #region positioning
        // postioning
        [A_XSDElementProperty("HorizontalPos", "UI", "\"Sets the position of the current control within it's parent. [0;1]. Works with non-container controls.\"")]
        public float horizontalPosition = 0.5f;

        [A_XSDElementProperty("VerticalPos", "UI", "Sets the position of the current control within it's parent. [0;1]. Works with non-container controls.")]
        public float verticalPosition = 0.5f;
        #endregion

        #region settings
        [A_XSDElementProperty("ScalingMode", "UI", "Sets how the control scales vertically within it's parent.")]
        public ScalingMode scalingMode = ScalingMode.None;

        [A_XSDElementProperty("ClipToBounds", "UI", "Will not render kids outside the bounds.")]
        public bool clipToBounds = false;

        [A_XSDElementProperty("DockMode", "UI", "Sets the control's dock mode to the desired setting. Fill - fills the entire area.")]
        public DockMode dockMode;

        [A_XSDElementProperty("StackIndex", "UI", "Tells the StackPanel parent (if its the parent) in which stack level the control should reside.")]
        public int stackIndex = 0;

        public VulkanControl? child;
        #endregion

        #region styling
        private string _controlColorHex = "#FFFFFF";
        [A_XSDElementProperty("ColorHex", "UI", "Sets the control color via hex code.")]
        public string controlColorHex
        {
            get => _controlColorHex;
            set
            {
                _controlColorHex = value;
                Vector3D<float> rgb = HexToRGB(value);
                controlData.style.tint = rgb;
                UpdateControlData();
            }
        }

        private ControlColor _color;
        [A_XSDElementProperty("ControlColor", "UI", "Sets the color of the control.")]
        public ControlColor controlColor
        {
            get => _color;
            set
            {
                string hex = EnumColorToHex(value);
                Vector3D<float> rgb = HexToRGB(hex);
                controlData.style.tint = rgb;
                UpdateControlData();
                _color = value;
                _controlColorHex = hex;
            }
        }
        #endregion

        #endregion

        #region rendering
        public ControlData controlData;
        public Buffer controlDataBuffer;
        public DeviceMemory controlDataBufferMemory;

        public Sampler maskSampler;
        public TextureAsset maskAsset;

        public Sampler colorSampler;
        public TextureAsset colorAsset;
        #endregion

        #region EVENTS
        //fuck do i do with this yet to figure out. tbh idk if this is even a problem
        public event Action<Vector2D<float>> hover;
        [A_XSDElementProperty("onEnter", "UI")]
        public Action onEnter;
        [A_XSDElementProperty("onExit", "UI")]
        public Action onExit;

        [A_XSDElementProperty("onClick", "UI")]
        public Action onClick;
        [A_XSDElementProperty("onAltClick", "UI")]
        public Action onAltClick;

        public Action onDoubleClick;

        [A_XSDElementProperty("onRelease", "UI")]
        public Action onRelease;
        [A_XSDElementProperty("onAltRelease", "UI")]
        public Action onAltRelease;

        public Action<Vector2D<float>, Vector2D<float>> onDrag;
        [A_XSDElementProperty("onDragStop", "UI")]
        public Action onDragStop;

        private bool entered = false;
        private bool clicked = false;
        private bool altClicked = false;
        private bool dragging = false;

        private DateTime lastClick = DateTime.Now;
        #endregion

        // EXTRAS
        public ContextMenuControl contextMenu;


        public VulkanControl()
        {
            controlData = new ControlData();
            controlData.style = ControlStyle.Default();
            controlData.uvs = new QuadUVs();

            maskAsset = AssetRegistries.GetAsset<TextureAsset>("default");

            ControlData tempData = controlData;
            AVulkanBufferHandler.CreateBuffer(ref tempData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
            CreateSampler();
            EntityManager.AddControl(this);
        }

        public override void OnStart()
        {
            base.OnStart();
        }

        private void CreateSampler()
        {
            Renderer.vk.GetPhysicalDeviceProperties(Renderer.gpu, out PhysicalDeviceProperties _properties);
            SamplerCreateInfo _createInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Nearest,
                MinFilter = Filter.Nearest,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                AnisotropyEnable = true,
                MaxAnisotropy = _properties.Limits.MaxSamplerAnisotropy,
                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Nearest
            };

            fixed (Sampler* _textureSamplerPtr = &maskSampler)
            {
                Result r = Renderer.vk.CreateSampler(Renderer.logicalDevice, ref _createInfo, null, _textureSamplerPtr);
                if (r != Result.Success)
                {
                    throw new Exception("Failed to create a texture sampler with error: " + r);
                }
            }
        }

        internal void UpdateControlData()
        {
            AVulkanBufferHandler.UpdateBuffer(ref controlData, ref controlDataBuffer, ref controlDataBufferMemory, BufferUsageFlags.StorageBufferBit);
        }

        public override void AddChild(Entity entity)
        {
            //vulkan control only
            if (entity is not VulkanControl control) throw new Exception("Child entity must be a VulkanControl");

            if (children.Count > 0)
            {
                throw new Exception("Control can only have one child");
            }
            else
            {
                children.Add(entity);
            }
            control.parent = this;

            // transform child
            Vector3D<float> transformedLoc = transform.position;
            if (control is not AbstractContainerControl container)
            {
                // map child horizontal and vertical pos to parent size
                transformedLoc.X += (control.horizontalPosition - 0.5f) * width;
                transformedLoc.Y += (control.verticalPosition - 0.5f) * height;
                //transformedLoc.Z = transform.position.Z;
            }
            control.transform.MoveToPosition(transformedLoc);
            control.SetControlScale(new Vector2D<float>(width, height));
        }

        //public virtual Vector2D<float> SetControlScale(Vector2D<float> availableSpace)

        public virtual void SetControlScale(Vector2D<float> availableSpace)
        {
            switch (scalingMode)
            {
                case ScalingMode.Uniform:
                    {
                        float aspect = (float)preferredWidth / (float)preferredHeight;
                        if (availableSpace.X / availableSpace.Y > aspect)
                        {
                            // limited by height
                            SetHeight((int)availableSpace.Y);
                            SetWidth((int)(availableSpace.Y * aspect));
                        }
                        else
                        {
                            // limited by width
                            SetWidth((int)availableSpace.X);
                            SetHeight((int)(availableSpace.X / aspect));
                        }
                        break;
                    }
                case ScalingMode.Fill:
                    {
                        SetSize(availableSpace);
                        break;
                    }
                case ScalingMode.Stretch:
                    {
                        SetWidth((int)availableSpace.X);
                        SetHeight((int)availableSpace.Y);
                        break;
                    }
                case ScalingMode.None:
                    {
                        SetSize(availableSpace);
                        break;
                    }
            }
        }

        #region size_setters
        public virtual void SetSize(Vector2D<float> size)
        {
            this.size = (Vector2D<int>)size;
        }
        public virtual void SetSize(Vector2D<int> size)
        {
            this.size = size;
        }
        public virtual void SetWidth(int x)
        {
            width = x;
        }
        public virtual void SetHeight(int y)
        {
            height = y;
        }
        #endregion


        #region mouse_events
        // HOVER
        public void RegisterHover(Action<Vector2D<float>> action)
        {
            hover += action;
        }

        public void ResolveHover(Vector2D<float> pos)
        {
            if (clicked)
            {
                dragging = true;
                UICollisionHandling.instance.dragging = this;
                return;
            }
            hover?.Invoke(pos);
        }

        // ENTER
        public void RegisterOnEnter(Action action)
        {
            onEnter += action;
        }

        public virtual void ResolveOnEnter()
        {
            if (!entered)
            {
                onEnter?.Invoke();
            }
            entered = true;
        }

        // EXIT
        public void RegisterOnExit(Action action)
        {
            onExit += action;
        }

        public virtual void ResolveExit()
        {
            if (entered)
            {
                onExit?.Invoke();
            }
            entered = false;
        }

        // DRAG
        public void RegisterOnDrag(Action<Vector2D<float>, Vector2D<float>> action)
        {
            onDrag += action;
        }

        public virtual void ResolveDrag(Vector2D<float> lastPos, Vector2D<float> delta)
        {
            //if (onDrag != null)
            //{
            onDrag?.Invoke(lastPos, delta);
            //}
        }

        public virtual void RegisterDragStop(Action action)
        {
            onDragStop += action;
        }

        public virtual void StopDrag()
        {
            onDragStop?.Invoke();
            UICollisionHandling.instance.dragging = null;
        }

        // CLICK
        public void RegisterOnClick(Action action)
        {
            onClick += action;
        }

        public virtual void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            if (!clicked)
            {
                DateTime click = DateTime.Now;
                TimeSpan span = click - lastClick;
                lastClick = click;
                if (span.TotalMilliseconds < Engine.doubleClickTime)
                {
                    ResolveOnDoubleClick();
                    clicked = true;
                    return;
                }

                onClick?.Invoke();
            }
            /*else
            {
                TimeSpan t = DateTime.Now - lastClick;
                if (t.TotalMilliseconds < Engine.doubleClickTime)
                    return;
                ResolveDrag(oldPos, delta);
            }*/
            clicked = true;
        }

        // DOUBLE CLICK
        public void RegisterOnDoubleClick(Action action)
        {
            onDoubleClick += action;
        }

        public virtual void ResolveOnDoubleClick()
        {
            onDoubleClick?.Invoke();
        }

        // RELEASE
        public void RegisterOnRelease(Action action)
        {
            onRelease += action;
        }

        public virtual void ResolveOnRelease()
        {
            if (dragging)
            {
                StopDrag();
            }
            if (clicked)
            {
                onRelease?.Invoke();
            }
            clicked = false;
        }

        // ALT CLICK
        public void RegisterOnAltClick(Action action)
        {
            onAltClick += action;
        }

        public virtual void ResolveOnAltClick()
        {
            if (!altClicked)
            {
                onAltClick?.Invoke();
            }
            altClicked = true;
        }

        // ALT RELEASE
        public void RegisterOnAltRelease(Action action)
        {
            onAltRelease += action;
        }

        public virtual void ResolveOnAltRelease()
        {
            if (altClicked)
            {
                onAltRelease?.Invoke();
            }
            altClicked = false;
        }
        #endregion


        public static string EnumColorToHex(ControlColor color)
        {
            return color switch
            {
                ControlColor.red => "#FF0000",
                ControlColor.green => "#00FF00",
                ControlColor.blue => "#0000FF",
                ControlColor.white => "#FFFFFF",
                ControlColor.black => "#000000",
                ControlColor.yellow => "#FFFF00",
                ControlColor.cyan => "#00FFFF",
                ControlColor.magenta => "#FF00FF",
                ControlColor.gray => "#808080",
                ControlColor.orange => "#FFA500",
                ControlColor.purple => "#800080",
                ControlColor.brown => "#A52A2A",
                ControlColor.pink => "#FFC0CB",
                ControlColor.lime => "#00FF00",
                ControlColor.navy => "#000080",
                ControlColor.teal => "#008080",
                _ => "#FFFFFF",
            };
        }

        public static Vector3D<float> HexToRGB(string hex)
        {
            if (hex.StartsWith("#"))
            {
                hex = hex[1..];
            }
            if (hex.Length != 6)
            {
                throw new ArgumentException("Hex color must be 6 characters long.");
            }
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Vector3D<float>(r / 255f, g / 255f, b / 255f);
        }

        public override void Invalidate()
        {
            base.Invalidate();
            foreach (Entity child in children)
            {
                child.Invalidate();
            }
        }

        public static VulkanControl ParseXML(string xmlName)
        {
            string path = Paths.XMLDOCUMENTS + "\\" + xmlName;
            XDocument doc = XDocument.Load(path);
            XElement root = doc.Root;
            WindowControl window = new WindowControl();
            ResolveAttributes(root, window);

            Vector3D<float> pos = new Vector3D<float>(window.width / 2f, window.height / 2f, -10.0f);
            window.transform.SetWorldPosition(pos);
            window.transform.SetWorldScale(new Vector3D<float>(window.width, window.height, 1));
            RecursiveParse(root, window);

            return window;
        }

        private static void RecursiveParse(XElement root, VulkanControl topControl)
        {
            foreach (var element in root.Elements())
            {
                Type type = AnyXMLType.FindType(element.Name.LocalName);
                var control = Activator.CreateInstance(type);
                ResolveAttributes(element, control);
                if (!typeof(VulkanControl).IsAssignableFrom(type))
                {
                    FieldInfo field = topControl.GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(f => f.FieldType.IsGenericType &&
                                             f.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                                             && f.FieldType.GetGenericArguments()[0].IsAssignableFrom(control.GetType()));
                    IList list = (IList)field.GetValue(topControl);

                    list.Add(control);
                    continue;
                }
                topControl.AddChild((VulkanControl)control);
                RecursiveParse(element, (VulkanControl)control);
            }
        }

        private static void ResolveAttributes(XElement root, object topControl)
        {
            foreach (XAttribute attr in root.Attributes())
            {
                var prop = topControl.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase).FirstOrDefault(m =>
                {
                    var a = m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).FirstOrDefault() as A_XSDElementPropertyAttribute;
                    if (a != null)
                    {
                        return string.Equals(a.Name, attr.Name.LocalName, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                });

                if (prop != null)
                {
                    Type memberType = prop.MemberType == MemberTypes.Field ? ((FieldInfo)prop).FieldType : ((PropertyInfo)prop).PropertyType;
                    if (memberType == typeof(Action))
                    {
                        MethodInfo? methodInfo = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        .FirstOrDefault(m =>
                                m.GetCustomAttributes(typeof(A_XSDActionDependencyAttribute), false).Any() &&
                                string.Equals(m.Name, attr.Value, StringComparison.OrdinalIgnoreCase));

                        if (methodInfo == null)
                            throw new Exception($"Action method '{attr.Value}' not found in A_XSDActionDependency.");

                        Action actionDelegate = (Action)Delegate.CreateDelegate(typeof(Action), methodInfo);
                        if (prop is PropertyInfo propertyInfo)
                        {
                            Action current = (Action?)propertyInfo.GetValue(topControl);
                            current += actionDelegate;
                            propertyInfo.SetValue(topControl, current);
                            continue;
                        }
                        if(prop is FieldInfo fieldInfo)
                        {
                            Action current = (Action?)fieldInfo.GetValue(topControl);
                            current += actionDelegate;
                            fieldInfo.SetValue(topControl, current);
                            continue;
                        }
                    }
                    else if (memberType.IsEnum)
                    {
                        if (prop is PropertyInfo propertyInfo)
                        {
                            object enumValue = Enum.Parse(propertyInfo.PropertyType, attr.Value);
                            propertyInfo.SetValue(topControl, enumValue);
                            continue;
                        }
                        if (prop is FieldInfo fieldInfo)
                        {
                            object enumValue = Enum.Parse(fieldInfo.FieldType, attr.Value);
                            fieldInfo.SetValue(topControl, enumValue);
                            continue;
                        }
                        continue;
                    }
                    else
                    {
                        if (prop is PropertyInfo propertyInfo)
                        {
                            object value = TypeDescriptor.GetConverter(propertyInfo.PropertyType).ConvertFromInvariantString(attr.Value);
                            propertyInfo.SetValue(topControl, value);
                            continue;
                        }
                        if (prop is FieldInfo fieldInfo)
                        {
                            object value = TypeDescriptor.GetConverter(fieldInfo.FieldType).ConvertFromInvariantString(attr.Value);
                            fieldInfo.SetValue(topControl, value);
                            continue;
                        }
                    }
                }
                if (topControl.GetType() == typeof(VulkanControl))
                {
                    ((VulkanControl)topControl).UpdateControlData();
                }
            }
        }

    }
}