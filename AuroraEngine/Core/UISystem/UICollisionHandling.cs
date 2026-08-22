using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Data;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using ScrollableControl = ArctisAurora.Core.UISystem.Controls.Containers.ScrollableControl;
using InputHandler = ArctisAurora.EngineWork.InputHandler;
using Keys = ArctisAurora.EngineWork.Keys;

namespace ArctisAurora.Core.UISystem
{
    public unsafe class UICollisionHandling
    {
        public static UICollisionHandling instance = null!;

        public Vector2D<float> lastMousePos;
        public Vector2D<float> delta;

        [A_ActiveContext("Hovering")]
        public static VulkanControl hovering { get; set; }
        [A_ActiveContext("Dragging")]
        public static VulkanControl dragging;
        // the control currently showing where the drag would land
        private static VulkanControl? hinted;
        
        /*[A_ActiveContext("ActiveContainer")]
        public static VulkanControl activeContainer;*/
        [A_ActiveContext("ActiveControl")]
        public static VulkanControl activeControl;

        // what the previous press resolved to, and whether this one landed on it again
        private static VulkanControl lastPressTarget;
        private static bool sameTargetTap;

        // a press that only took a menu down, so the release it pairs with is not a click either
        private static bool pressSwallowed;


        public UICollisionHandling()
        {
            instance = this;
        }

        // The root is the tree of whichever window the pointer is in — one pointer, so the hover,
        // drag and active contexts below stay global.
        public void SolveHover(Vector2D<float> mousePos, VulkanControl root)
        {
            if (root == null) return;

            // An open menu takes the whole pointer — nothing under it answers while it is up.
            root = ContextMenus.OpenIn(root) ?? root;

            Vector2D<float>[] localVerts = new Vector2D<float>[4];

            VulkanControl deepest = FindDeepestValid(mousePos, root, ref localVerts);
            if (deepest != root && deepest != null)
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

        public void SolveLMBPress(Vector2D<float> mousePos, VulkanControl root)
        {
            pressSwallowed = ContextMenus.DismissedBy(root, mousePos);
            if (pressSwallowed) return;
            if (hovering == null) return;

            VulkanControl target = ActiveTarget(hovering);
            sameTargetTap = ReferenceEquals(target, lastPressTarget);
            lastPressTarget = target;

            SetActiveControl(target);
            hovering?.ResolveOnClick(lastMousePos, delta);
        }

        // Also called without a click behind it, by a prompt that has to hold the active context
        // before the pointer ever reaches it — otherwise its keystrokes go to whatever was clicked
        // last.
        public static void SetActiveControl(VulkanControl control)
        {
            if (ReferenceEquals(activeControl, control)) return;

            VulkanControl previous = activeControl;
            activeControl = control;
            (previous as IContext)?.OnContextRemoved("ActiveControl");
            (control as IContext)?.OnContextAdded("ActiveControl");
        }

        public void SolveLMBRelease(Vector2D<float> mousePos, int tapCount)
        {
            if (pressSwallowed)
            {
                pressSwallowed = false;
                return;
            }

            VulkanControl dragTarget = dragging;
            if (dragTarget != null)
            {
                // cleared before the callbacks, so a handler asking whether a drag is live gets no
                SetDragging(null);
                DragGhost.Hide();
                ClearDropHint();
                OfferDrop(dragTarget);
                dragTarget.StopDrag();
                dragTarget.ResolveOnRelease();
            }
            else if (ActiveTarget(hovering) == activeControl)
            {
                hovering?.ResolveOnRelease();
            }

            if (tapCount == 2 && sameTargetTap && ActiveTarget(hovering) == activeControl)
                hovering?.ResolveOnDoubleClick();
        }

        public void SolveRMBPress(Vector2D<float> mousePos, VulkanControl root)
        {
            if (ContextMenus.DismissedBy(root, mousePos)) return;
            if (hovering == null) return;
            hovering?.ResolveOnAltClick();
            hovering.OpenContextMenu();
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
                DragGhost.Hide();
                ClearDropHint();
                stale.StopDrag();
                return;
            }

            dragging.ResolveDrag(lastMousePos, delta);
            UpdateDropHint(dragging);
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
            if (ReferenceEquals(hinted, control)) hinted = null;
            if (ReferenceEquals(activeControl, control)) activeControl = null;
            if (ReferenceEquals(lastPressTarget, control)) lastPressTarget = null;
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

        // The control under the pointer mid-drag, and the point in its window's design space.
        //
        // The target cannot come from `hovering`: the press captured the pointer to the drag's own
        // window, so no other window is told the pointer is over it and its tree is never hovered.
        // The drag's window does still report accurate positions, so the target is found by geometry
        // — screen point, the window whose rect holds it, that window's tree.
        private static VulkanControl HitFor(VulkanControl dropped, out Vector2D<float> local)
        {
            local = Vector2D<float>.Zero;

            RenderWindow source = RenderWindow.Of(dropped);
            if (source == null) return null;

            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            Vector2D<float> screen = new Vector2D<float>(sx + source.mousePos.X, sy + source.mousePos.Y);

            RenderWindow target = WindowAt(screen);
            if (target == null || target.ui.uiRoot == null) return null;

            AGlfwWindow._glfw.GetWindowPos(target.os.handle, out int tx, out int ty);
            local = target.ui.ToDesignSpace(new Vector2D<float>(screen.X - tx, screen.Y - ty));

            return instance.HitTest(local, target.ui.uiRoot);
        }

        // Offers the dropped control to whatever is under the pointer, innermost first.
        private static void OfferDrop(VulkanControl dropped)
        {
            VulkanControl control = HitFor(dropped, out Vector2D<float> local);
            while (control != null)
            {
                if (control.ResolveDrop(dropped, local)) return;
                control = control.parent as VulkanControl;
            }
        }

        // Asks the same walk to show where the drop would land, once per tick the drag runs.
        private static void UpdateDropHint(VulkanControl dropped)
        {
            VulkanControl control = HitFor(dropped, out Vector2D<float> local);
            VulkanControl next = null;

            while (control != null)
            {
                if (control.ResolveDropHint(dropped, local))
                {
                    next = control;
                    break;
                }
                control = control.parent as VulkanControl;
            }

            if (!ReferenceEquals(hinted, next)) hinted?.ClearDropHint();
            hinted = next;
        }

        private static void ClearDropHint()
        {
            hinted?.ClearDropHint();
            hinted = null;
        }

        // First window whose rect holds the point. Overlapping windows are resolved by map order,
        // not by what is actually on top — GLFW publishes no z-order.
        private static RenderWindow WindowAt(Vector2D<float> screen)
        {
            foreach (RenderWindow window in Engine.windows.Values)
            {
                // the preview sits under the pointer by definition, so it must never be a drop target
                if (window.closeRequested || window.isGhost) continue;

                AGlfwWindow._glfw.GetWindowPos(window.os.handle, out int x, out int y);
                Extent2D size = window.os.windowSize;
                if (screen.X >= x && screen.Y >= y && screen.X < x + size.Width && screen.Y < y + size.Height)
                    return window;
            }
            return null;
        }

        // The deepest hit-testable control under a point in one tree.
        public VulkanControl HitTest(Vector2D<float> point, VulkanControl root)
        {
            Vector2D<float>[] localVerts = new Vector2D<float>[4];
            return FindDeepestValid(point, root, ref localVerts);
        }

        // Walks up to the first control that can hold the active context.
        private static VulkanControl ActiveTarget(VulkanControl control)
        {
            while (control != null && !control.canBeActiveContext)
                control = control.parent as VulkanControl;
            return control;
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
