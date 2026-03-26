using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.AssetRegistry;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;

namespace ArctisAurora.Core.UISystem
{
    public unsafe class UICollisionHandling
    {
        public static UICollisionHandling instance;
        public bool isInWindow = true;
        //public AbstractContainerControl container;
        public ContextMenuControl defaultContextMenu;

        public Vector2D<float> lastMousePos;
        public Vector2D<float> delta;

        [A_ActiveContext("Hovering")]
        public static VulkanControl hovering { get; set; }
        [A_ActiveContext("Draggin")]
        public static VulkanControl dragging;
        
        /*[A_ActiveContext("ActiveContainer")]
        public static VulkanControl activeContainer;
        [A_ActiveContext("ActiveControl")]
        public static VulkanControl activeControl;*/


        public UICollisionHandling()
        {
            instance = this;
        }

        public void SolveHover(Vector2D<float> mousePos)
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

        public void SolveLMBPress(Vector2D<float> mousePos)
        {
            if (hovering == null) return;
            hovering?.ResolveOnClick(lastMousePos, delta);
        }

        public void SolveLMBRelease(Vector2D<float> mousePos)
        {
            VulkanControl dragTarget = dragging;
            if (dragTarget != null)
            {
                dragTarget.StopDrag();
                dragTarget.ResolveOnRelease();
            }
            else
            {
                hovering?.ResolveOnRelease();
            }
        }

        public void SolveRMBPress(Vector2D<float> mousePos)
        {
            if (hovering == null) return;
            hovering?.ResolveOnAltClick();
        }

        public void SolveRMBRelease(Vector2D<float> mousePos)
        {
            if (hovering == null) return;
            hovering?.ResolveOnAltRelease();
        }
        
        public void SolveDrag(Vector2D<float> mousePos)
        {
            if (dragging != null)
                dragging.ResolveDrag(lastMousePos, delta);
        }

        public void SolveScroll(Vector2D<float> offset)
        {
            VulkanControl target = hovering;
            while (target != null)
            {
                // Let individual controls consume scroll first (e.g. spinners, sliders)
                if (offset.Y > 0)
                {
                    if (target.ResolveOnScrollUp()) return;
                }
                else if (offset.Y < 0)
                {
                    if (target.ResolveOnScrollDown()) return;
                }

                // If not consumed, check for a scrollable container
                if (target is ScrollableControl scroll)
                {
                    // GLFW: positive Y = scroll up, negative Y = scroll down
                    // OnScrollInput expects positive = scroll content down (increase offset)
                    // So we negate: scroll wheel up → content moves down → negative deltaY
                    scroll.OnScrollInput(offset.X, -offset.Y);
                    return;
                }

                target = target.parent as VulkanControl;
            }
        }

        #region ---- HELPERS ----
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
        #endregion
    }
}
