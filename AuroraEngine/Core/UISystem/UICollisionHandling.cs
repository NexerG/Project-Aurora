using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;
using InputHandler = ArctisAurora.EngineWork.InputHandler;
using Keys = ArctisAurora.EngineWork.Keys;

namespace ArctisAurora.Core.UISystem
{
    public unsafe class UICollisionHandling
    {
        public static UICollisionHandling instance;
        public bool isInWindow = true;
        public ContextMenuControl defaultContextMenu;

        public Vector2D<float> lastMousePos;
        public Vector2D<float> delta;

        [A_ActiveContext("Hovering")]
        public static VulkanControl hovering { get; set; }
        [A_ActiveContext("Dragging")]
        public static VulkanControl dragging;
        
        /*[A_ActiveContext("ActiveContainer")]
        public static VulkanControl activeContainer;*/
        [A_ActiveContext("ActiveControl")]
        public static VulkanControl activeControl;


        public UICollisionHandling()
        {
            instance = this;
        }

        public void SolveHover(Vector2D<float> mousePos)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];

            VulkanControl deepest = FindDeepestValid(mousePos, EntityRegistry.uiTree, ref localVerts);
            if (deepest != EntityRegistry.uiTree && deepest != null)
            {
                if (deepest != hovering)
                {
                    (hovering as IContext)?.OnContextRemoved("Hovering");
                    hovering?.ResolveExit();
                    Context.Set("Hovering", deepest);
                    (deepest as IContext)?.OnContextAdded("Hovering");
                    deepest.ResolveOnEnter();
                }
                deepest.ResolveHover(mousePos);
            }
            else
            {
                deepest = Context.Get<VulkanControl>("Hovering");
                if (deepest != null)
                {
                    (deepest as IContext)?.OnContextRemoved("Hovering");
                    deepest.ResolveExit();
                }
                Context.Clear("Hovering");
            }
        }

        public void SolveLMBPress(Vector2D<float> mousePos)
        {
            if (hovering == null) return;
            VulkanControl target = ActiveTarget(hovering);
            if (activeControl != target)
            {
                (activeControl as IContext)?.OnContextRemoved("ActiveControl");
                activeControl = target;
                (activeControl as IContext)?.OnContextAdded("ActiveControl");
            }
            hovering?.ResolveOnClick(lastMousePos, delta);
        }

        public void SolveLMBRelease(Vector2D<float> mousePos)
        {
            VulkanControl dragTarget = dragging;
            if (dragTarget != null)
            {
                // cleared before the callbacks, so a handler asking whether a drag is live gets no
                SetDragging(null);
                dragTarget.StopDrag();
                dragTarget.ResolveOnRelease();
            }
            else if (ActiveTarget(hovering) == activeControl)
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
            if (dragging == null) return;

            // A release nobody saw. HandleUI returns before this whole block while the pointer is
            // outside the window, so a button that comes up out there never reaches SolveLMBRelease
            // and justReleased is gone by the next tick — the drag would stay live for good.
            if (!InputHandler.instance.IsKeyDown(Keys.MouseLeft))
            {
                VulkanControl stale = dragging;
                SetDragging(null);
                stale.StopDrag();
                return;
            }

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
            // The clip rect is inherited, so a point outside it rules out the whole subtree — a row
            // scrolled past the top of its viewport is drawn nowhere and must be clickable nowhere.
            if (!current.HitTest(mousePos))
                return null;

            if (!SolvePositions(current, mousePos, localVerts))
                return null;

            foreach (VulkanControl child in current.GetAllChildrenEntities())
            {
                if (!child.hitTestable) continue;

                VulkanControl? deeper = FindDeepestValid(mousePos, child, ref localVerts);
                if (deeper != null)
                    return deeper;
            }
            return current;
        }

        // Drops a destroyed control out of every context holding it. Assigns directly rather than
        // through SetDragging — the control is going away, so notifying it is the thing to avoid.
        public static void Forget(VulkanControl control)
        {
            if (ReferenceEquals(hovering, control)) hovering = null;
            if (ReferenceEquals(dragging, control)) dragging = null;
            if (ReferenceEquals(activeControl, control)) activeControl = null;
        }

        // Assigns the drag context and notifies both sides.
        public static void SetDragging(VulkanControl control)
        {
            if (dragging == control) return;

            VulkanControl previous = dragging;
            dragging = control;
            (previous as IContext)?.OnContextRemoved("Dragging");
            (control as IContext)?.OnContextAdded("Dragging");
        }

        // Walks up to the first control that can hold the active context.
        private static VulkanControl ActiveTarget(VulkanControl control)
        {
            while (control != null && !control.canBeActiveContext)
                control = control.parent as VulkanControl;
            return control;
        }

        public void IsInWindow(WindowHandle* handle, bool isInWindow)
        {
            this.isInWindow = isInWindow;
        }

        private bool SolvePositions(VulkanControl entity, Vector2D<float> pos, Vector2D<float>[] localVerts)
        {
            // A collapsed quad passes the edge test for every point on the plane — every cross
            // product is zero, so "all on the same side" is vacuously true — which turns a control
            // arranged to nothing into one that swallows the entire hit-test.
            if (entity.transform.scale.X == 0f || entity.transform.scale.Y == 0f) return false;

            localVerts[0] = new Vector2D<float>(-0.5f, -0.5f);
            localVerts[1] = new Vector2D<float>(0.5f, -0.5f);
            localVerts[2] = new Vector2D<float>(0.5f, 0.5f);
            localVerts[3] = new Vector2D<float>(-0.5f, 0.5f);

            localVerts = TransformToWorld(entity.transform, localVerts);
            return IsPointInQuad(pos, localVerts);
        }

        private Vector2D<float>[] TransformToWorld(TransformData transform, Vector2D<float>[] localVerts)
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
