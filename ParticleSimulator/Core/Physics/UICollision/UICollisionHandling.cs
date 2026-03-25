using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork.AssetRegistry;
using Silk.NET.GLFW;
using Silk.NET.Maths;

namespace ArctisAurora.EngineWork.Physics.UICollision
{
    public unsafe class UICollisionHandling
    {
        public static UICollisionHandling instance;
        public bool isInWindow = true;
        public VulkanControl dragging;
        public AbstractContainerControl container;
        public ContextMenuControl defaultContextMenu;

        public Vector2D<float> lastMousePos;
        public Vector2D<float> delta;

        [A_ActiveContext("Hovering")]
        public static VulkanControl hovering { get; set; }
        [A_ActiveContext("ActiveContainer")]
        public static VulkanControl activeContainer;
        [A_ActiveContext("ActiveControl")]
        public static VulkanControl activeControl;


        public UICollisionHandling()
        {
            instance = this;
        }

        public void SolverHover(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];

            VulkanControl deepest = FindDeepestValid(mousePos, EntityManager.uiTree, ref localVerts);
            if (deepest != EntityManager.uiTree && deepest != null)
            {
                Context.Set("Hovering", deepest);
                deepest.ResolveHover(mousePos);
            }
            else
            {
                deepest = Context.Get<VulkanControl>("Hovering");
                if(deepest != null)
                    deepest.ResolveExit();
                Context.Clear("Hovering");
            }
        }

        public void SolveLMB(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];
            bool pressed = InputHandler.instance.IsKeyDown(new Keybind(Keybind.MouseKey(MouseButton.Left)));

            VulkanControl mostDeep = null;
            VulkanControl top = EntityManager.uiTree;
            if (pressed && SolvePositions(EntityManager.uiTree, mousePos, localVerts))
            {
                mostDeep = EntityManager.uiTree;
                foreach (VulkanControl child in top.GetAllChildrenEntities())
                {
                    bool isHovering = SolvePositions(child, mousePos, localVerts);
                    if (isHovering)
                    {
                        mostDeep = child;
                    }
                    else
                    {
                        child.ResolveOnRelease();
                    }
                }
            }
            else if (EntityManager.uiTree != null)
            {
                EntityManager.uiTree.ResolveExit();
            }
            
            if (mostDeep != null && dragging == null)
            {
                mostDeep.ResolveOnClick(lastMousePos, delta);
            }
        }

        public void SolveRMB(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];
            bool pressed = InputHandler.instance.IsKeyDown(new Keybind(Keybind.MouseKey(MouseButton.Right)));

            VulkanControl mostDeep = null;
            VulkanControl top = EntityManager.uiTree;
            if (pressed && SolvePositions(EntityManager.uiTree, mousePos, localVerts))
            {
                mostDeep = EntityManager.uiTree;
                foreach (VulkanControl child in top.GetAllChildrenEntities())
                {
                    bool isHovering = SolvePositions(child, mousePos, localVerts);
                    if (isHovering)
                    {
                            mostDeep = child;
                    }
                    else
                    {
                            child.ResolveOnAltRelease();
                    }
                }
            }
            else if (EntityManager.uiTree != null)
            {
                EntityManager.uiTree.ResolveExit();
            }
            
            if (mostDeep != null)
            {
                mostDeep.ResolveOnAltClick();
            }
        }

        public void SolveDrag(Vector2D<float> mousePos)
        {
            if (dragging != null)
            {
                dragging.ResolveDrag(lastMousePos, delta);
            }
        }


        private VulkanControl FindDeepestValid(Vector2D<float> mousePos, VulkanControl current, ref Vector2D<float>[] localVerts)
        {
            if (!SolvePositions(current, mousePos, localVerts))
                return null;

            foreach (VulkanControl child in current.GetAllChildrenEntities())
            {
                VulkanControl? deeper = FindDeepestValid(mousePos, child, ref localVerts);
                if (deeper != null)
                    return deeper;
            }
            return current;
        }

        public void IsInWindow(WindowHandle* handle, bool isInWindow)
        {
            this.isInWindow = isInWindow;
        }

        private bool SolvePositions(VulkanControl entity, Vector2D<float> pos, Vector2D<float>[] localVerts)
        {
            localVerts[0] = new Vector2D<float>(-0.5f, -0.5f);
            localVerts[1] = new Vector2D<float>(0.5f, -0.5f);
            localVerts[2] = new Vector2D<float>(0.5f, 0.5f);
            localVerts[3] = new Vector2D<float>(-0.5f, 0.5f);

            localVerts = TransformToWorld(entity.transform, localVerts);
            return IsPointInQuad(pos, localVerts);
        }

        private Vector2D<float>[] TransformToWorld(Transform transform, Vector2D<float>[] localVerts)
        {
            Vector2D<float>[] worldVerts = new Vector2D<float>[4];

            //float cos = MathF.Cos(transform.rotation);
            //float sin = MathF.Sin(transform.rotation);

            for (int i = 0; i < 4; i++)
            {
                Vector2D<float> scaled = new Vector2D<float>(localVerts[i].X * transform.scale.X, localVerts[i].Y * transform.scale.Y);
                worldVerts[i] = new Vector2D<float>(scaled.X + transform.position.X, scaled.Y + transform.position.Y);
            }

            return worldVerts;
        }

        private bool IsPointInQuad(Vector2D<float> point, Vector2D<float>[] quadVerts)
        {
            bool sameSide = true;

            for (int i = 0; i < 4; i++)
            {
                Vector2D<float> a = quadVerts[i];
                Vector2D<float> b = quadVerts[(i + 1) % 4];
                Vector2D<float> edge = b - a;
                Vector2D<float> toPoint = point - a;

                float cross = edge.X * toPoint.Y - edge.Y * toPoint.X;

                if (i == 0)
                    sameSide = cross > 0;
                else if ((cross > 0) != sameSide)
                    return false;
            }

            return true;
        }

        public void SetContext()
        {
            throw new NotImplementedException();
        }
    }
}
