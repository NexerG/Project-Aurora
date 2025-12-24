using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Physics.UICollision;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace ArctisAurora.EngineWork.Rendering.UI.Controls
{
    #region control_attributes
    /// <summary>
    /// Used to create an element. Example: <Button></Button>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class A_VulkanControlAttribute : Attribute
    {
        public string Name { get; }
        public A_VulkanControlAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Used to create a property for a vulkan control element. Example: <Button OnEnter="[event name]"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Property, Inherited = false)]
    public sealed class A_VulkanControlPropertyAttribute: Attribute
    {
        public string Name { get; }
        public string Description { get; set; } = "";

        public A_VulkanControlPropertyAttribute(string name)
        {
            Name = name;
        }

        public A_VulkanControlPropertyAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Used to create an entry for a vulkan control element that shouldnt be used as a general element. Example: <Grid> <Grid.RowSettings/> </Grid>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class A_VulkanControlElementAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; set; } = "";
        public A_VulkanControlElementAttribute(string name)
        {
            Name = name;
        }
        public A_VulkanControlElementAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Used to create an enum for vulkan controls. Example: <Button Color="Red"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, Inherited = false)]
    public sealed class A_VulkanEnumAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; set; } = "";

        public A_VulkanEnumAttribute(string name)
        {
            Name = name;
        }

        public A_VulkanEnumAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
    
    /// <summary>
    /// Used to create an action for a vulkan control. Example: <Button OnClick="[event name]"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class A_VulkanActionAttribute : Attribute
    { }
    #endregion

    public unsafe class VulkanControl : Entity
    {
        #region STRUCTS
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ControlStyle
        {
            public Vector3D<float> tint;
            //public Sampler image;
            //public Sampler mask;

            public static ControlStyle Default()
            {
                Dictionary<string, ControlStyle> dStyles = AssetRegistries.GetRegistry<string, ControlStyle>(typeof(ControlStyle));
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

        [A_VulkanEnum("ControlColor")]
        public enum ControlColor
        {
            red, green, blue, white, black, yellow, cyan, magenta, gray, orange, purple, brown, pink, lime, navy, teal,
        }

        [A_VulkanEnum("FillMode")]
        public enum ScalingMode
        {
            Uniform,
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

        #region properties
        // sizing
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


        [A_VulkanControlProperty("Width", "Width in pixels")]
        public int preferredWidth = 72;
        [A_VulkanControlProperty("Height", "Height in pixels")]
        public int preferredHeight = 72;

        [A_VulkanControlProperty("MinHeight", "Minimum height in pixels")]
        public int minHeight = 0;
        [A_VulkanControlProperty("MinWidth", "Minimum width in pixels")]
        public int minWidth = 0;

        [A_VulkanControlProperty("ScalingMode", "Sets how the control scales vertically within it's parent.")]
        public ScalingMode scalingMode = ScalingMode.None;

        /*public new VulkanControl parent
        {
            get => (VulkanControl)base.parent;
            set
            {
                base.parent = value;
            }
        }*/

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

        // postioning
        [A_VulkanControlProperty("HorizontalPos", "Sets the position of the current control within it's parent. [0;1]. Works with non-container controls.")]
        public float horizontalPosition = 0.5f;

        [A_VulkanControlProperty("VerticalPos", "Sets the position of the current control within it's parent. [0;1]. Works with non-container controls.")]
        public float verticalPosition = 0.5f;

        [A_VulkanControlProperty("ClipToBounds", "Clips child controls to the bounds of this control.")]
        public bool clipToBounds = false;

        // settings
        [A_VulkanControlProperty("DockMode")]
        public DockMode dockMode;
        [A_VulkanControlProperty("ControlColor")]
        public ControlColor controlColor
        {
            get => color;
            set
            {
                string hex = EnumColorToHex(value);
                Vector3D<float> rgb = HexToRGB(hex);
                controlData.style.tint = rgb;
                UpdateControlData();
            }
        }
        [A_VulkanControlProperty("StackIndex", "Tells the StackPanel parent (if its the parent) in which stack level the control should reside.")]
        public int stackIndex = 0;

        public VulkanControl? child;
        private ControlColor color;

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
        [A_VulkanControlProperty("onEnter")]
        public Action onEnter;
        [A_VulkanControlProperty("onExit")]
        public Action onExit;

        [A_VulkanControlProperty("onClick")]
        public Action onClick;
        [A_VulkanControlProperty("onAltClick")]
        public Action onAltClick;

        public Action onDoubleClick;

        [A_VulkanControlProperty("onRelease")]
        public Action onRelease;
        [A_VulkanControlProperty("onAltRelease")]
        public Action onAltRelease;

        public Action<Vector2D<float>, Vector2D<float>> onDrag;
        [A_VulkanControlProperty("onDragStop")]
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
                // map chil horizontal and vertical pos to parent size
                transformedLoc.X += (control.horizontalPosition - 0.5f) * width;
                transformedLoc.Y += (control.verticalPosition - 0.5f) * height;
                //transformedLoc.Z = transform.position.Z;
            }
            control.transform.SetWorldPosition(transformedLoc);

            control.SetControlScale(new Vector2D<float>(width, height));
        }

        public virtual void SetControlScale(int availableWidth, int availableHeight)
        {
            switch (scalingMode)
            {
                case ScalingMode.Uniform:
                    {
                        float parentAspect = (float)availableWidth / (float)availableHeight;
                        if (parentAspect >= 1)
                        {
                            SetHeight(availableHeight);
                            SetWidth((int)(availableHeight * parentAspect));
                        }
                        else
                        {
                            SetWidth(availableWidth);
                            SetHeight((int)(availableWidth / parentAspect));
                        }
                        break;
                    }
                case ScalingMode.Fill:
                    {
                        SetWidth(availableWidth);
                        SetHeight(availableHeight);
                        break;
                    }
                case ScalingMode.None:
                    {
                        SetSize(new Vector2D<int>(availableWidth, availableHeight));
                        break;
                    }
            }
        }

        public virtual void SetControlScale(Vector2D<float> availableSpace)
        {
            switch(scalingMode)
            {
                case ScalingMode.Uniform:
                    {
                        float parentAspect = availableSpace.X / availableSpace.Y;
                        if (parentAspect >= 1)
                        {
                            SetHeight((int)availableSpace.Y);
                            SetWidth((int)(availableSpace.Y * parentAspect));
                        }
                        else
                        {
                            SetWidth((int)availableSpace.X);
                            SetHeight((int)(availableSpace.X / parentAspect));
                        }
                        break;
                    }
                case ScalingMode.Fill:
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

        public virtual void ResolveEnter()
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

        public virtual void ResolveClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            if (!clicked)
            {
                DateTime click = DateTime.Now;
                TimeSpan span = click - lastClick;
                lastClick = click;
                if (span.TotalMilliseconds < Engine.doubleClickTime)
                {
                    ResolveDoubleClick();
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
        public void RegisterDoubleClick(Action action)
        {
            onDoubleClick += action;
        }

        public virtual void ResolveDoubleClick()
        {
            onDoubleClick?.Invoke();
        }

        // RELEASE
        public void RegisterOnRelease(Action action)
        {
            onRelease += action;
        }

        public virtual void ResolveRelease()
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
        public void RegisterAltClick(Action action)
        {
            onAltClick += action;
        }

        public virtual void ResolveAltClick()
        {
            if (!altClicked)
            {
                onAltClick?.Invoke();
            }
            altClicked = true;
        }

        // ALT RELEASE
        public void RegisterAltRelease(Action action)
        {
            onAltRelease += action;
        }

        public virtual void ResolveAltRelease()
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
    }
}