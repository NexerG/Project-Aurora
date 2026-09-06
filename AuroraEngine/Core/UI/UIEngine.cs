using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Registry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArctisAurora.Core.UI
{
    // Entry point of the UI, on the main thread.
    public static class UIEngine
    {
        private static readonly LogChannel Log = LogChannel.For("UIEngine");

        private static DataPool _elements;
        private static DataPool _controls;

        public static DataPool Elements => _elements ??= DataManager.Get("UIElements");
        public static DataPool Controls => _controls ??= DataManager.Get("VulkanControls");

        #region ---- layout ----
        private static readonly HashSet<Control> _dirtyRoots = new HashSet<Control>();

        public static void RegisterDirtyRoot(Control control) => _dirtyRoots.Add(control);

        // Measures and arranges every dirty subtree, then refreshes its collision caches.
        public static void ResolveLayout()
        {
            if (_dirtyRoots.Count == 0) return;

            Control[] roots = new Control[_dirtyRoots.Count];
            _dirtyRoots.CopyTo(roots);
            _dirtyRoots.Clear();

            foreach (Control root in roots)
            {
                if (root.isMeasureDirty)
                {
                    // Pass 1 — offer the root its own current arranged size, or infinite if it has
                    // never been arranged.
                    Vector2D<float> offer = root.arrangedRect.size == Vector2D<float>.Zero
                        ? new Vector2D<float>(float.MaxValue, float.MaxValue)
                        : root.arrangedRect.size;

                    root.Measure(offer);

                    // Pass 2 — re-arrange from the root's current rect. A window root is fitted
                    // externally, on resize.
                    LayoutRect finalRect = root.arrangedRect.size == Vector2D<float>.Zero
                        ? new LayoutRect(0, 0, root.DesiredSize.X, root.DesiredSize.Y)
                        : root.arrangedRect;

                    root.Arrange(finalRect);
                }
                else if (root.isArrangeDirty)
                {
                    root.Arrange(root.arrangedRect);
                }

                root.RefreshSubtreeCache();
                VerifySubtreeCache(root);
            }
        }

        // Recomputes the caches independently and reports a mismatch. The maintained values are the
        // hit-test's early-out, so a wrong one loses clicks rather than drawing anything odd.
        [Conditional("DEBUG")]
        private static void VerifySubtreeCache(Control control)
        {
            LayoutRect bounds = control.arrangedRect;
            int count = 1;

            foreach (Entity e in control.children)
            {
                if (e is not Control child) continue;

                VerifySubtreeCache(child);
                bounds = LayoutRect.Union(bounds, child.arrange.subtreeBounds);
                count += child.arrange.subtreeCount;
            }

            ref ArrangeData a = ref control.arrange;
            if (a.subtreeCount != count)
                Log.Error($"'{control.name}' subtreeCount {a.subtreeCount} != recomputed {count}");
            if (a.subtreeBounds.x != bounds.x || a.subtreeBounds.y != bounds.y
                || a.subtreeBounds.width != bounds.width || a.subtreeBounds.height != bounds.height)
                Log.Error($"'{control.name}' subtreeBounds ({a.subtreeBounds.x}, {a.subtreeBounds.y}, " +
                          $"{a.subtreeBounds.width}, {a.subtreeBounds.height}) != recomputed " +
                          $"({bounds.x}, {bounds.y}, {bounds.width}, {bounds.height})");
        }
        #endregion

        #region ---- input ----
        // One pointer, so these are global. The names carry a Next prefix because A_ActiveContext
        // registers by name into one dictionary and the outgoing stack owns the plain ones — landing
        // 6 renames them when it deletes Core.UISystem.
        [A_ActiveContext("NextHovering")]
        public static Control hovering { get; set; }

        [A_ActiveContext("NextActiveControl")]
        public static Control activeControl { get; set; }

        [A_ActiveContext("NextPressTarget")]
        public static Control pressTarget { get; set; }

        // The control that claimed the drag. Nothing delivers to it yet — see the drag gap in
        // ClaudeMemory/Decisions/ui-engine-stack.md.
        [A_ActiveContext("NextDragging")]
        public static Control dragging { get; set; }

        // what the drag is currently over, so it can be told when the drag leaves it
        private static Control _dragTarget;

        // the drag target's own window-space point, so the release does not have to recompute it
        private static Vector2D<float> _dragPoint;

        // The window the pointer is over while a drag runs, null when it is over none or when an
        // overlap makes the answer ambiguous. Only meaningful mid-drag — a captured pointer is what
        // makes the geometry search necessary, and it costs a position query per window per tick.
        [A_ActiveContext("NextMouseOverWindow")]
        public static RenderWindow mouseOverWindow { get; set; }

        [A_ActiveContext("NextActiveWindow")]
        public static RenderWindow activeWindow { get; set; }

        private static Vector2D<float> _lastPoint;
        private static bool _sameTargetTap;

        // Resolves the pointer against one window's tree and dispatches what the buttons did.
        public static void Poll(RenderWindow window)
        {
            WindowRoot root = window.uiNext?.uiRoot;
            if (root == null) return;

            Vector2D<float> point = root.ToDesignSpace(window.mousePos, window.os.windowSize);
            Vector2D<float> delta = point - _lastPoint;
            _lastPoint = point;

            // A pressed button captures the pointer to the window it went down in, which keeps
            // reporting positions far outside itself and stops every other window hearing anything.
            // So the drag's own window drives the whole gesture, wherever the pointer has gone.
            bool ownsDrag = dragging != null && ReferenceEquals(WindowOf(dragging), window);

            // The pointer left this window, so nothing of ours is under it any more. Scoped to our
            // own tree, because every window polls and the contexts are global.
            if (!window.isInWindow && !ownsDrag)
            {
                if (RootOf(hovering) == root) SetHovering(null, point, delta);
                return;
            }

            if (window.isInWindow) SolveHover(point, delta, root);

            if (ownsDrag) SolveDragWindow(window);

            KeyStateEntry lmb = InputHandler.instance.keyTracker.GetState(Keys.MouseLeft);
            KeyStateEntry rmb = InputHandler.instance.keyTracker.GetState(Keys.MouseRight);

            if (lmb != null)
            {
                if (lmb.justPressed) SolvePress(point, delta, PointerEvent.leftButton);
                if (lmb.justReleased) SolveRelease(point, delta, PointerEvent.leftButton, lmb.tapCount);
            }

            if (rmb != null)
            {
                if (rmb.justPressed) SolvePress(point, delta, PointerEvent.rightButton);
                if (rmb.justReleased) SolveRelease(point, delta, PointerEvent.rightButton, rmb.tapCount);
            }

            if (ownsDrag) CheckDrag(window);

            if (window.scrollDelta.X != 0 || window.scrollDelta.Y != 0)
                SolveScroll(point, window.scrollDelta);
        }

        private static void SolveHover(Vector2D<float> point, Vector2D<float> delta, WindowRoot root)
        {
            Control deepest = HitTest(root, point);
            if (ReferenceEquals(deepest, root)) deepest = null;

            SetHovering(deepest, point, delta);

            if (hovering != null)
                Dispatch(Event(hovering, point, delta, 0, 0), PointerPhase.Move);
        }

        private static void SetHovering(Control control, Vector2D<float> point, Vector2D<float> delta)
        {
            if (ReferenceEquals(hovering, control)) return;

            Control previous = hovering;
            (previous as IContext)?.OnContextRemoved("NextHovering");
            if (previous != null)
                Dispatch(Event(previous, point, delta, 0, 0), PointerPhase.Exit);

            Context.Set("NextHovering", control);
            (control as IContext)?.OnContextAdded("NextHovering");
            if (control != null)
                Dispatch(Event(control, point, delta, 0, 0), PointerPhase.Enter);
        }

        private static void SolvePress(Vector2D<float> point, Vector2D<float> delta, int button)
        {
            if (hovering == null) return;

            if (button == PointerEvent.leftButton)
            {
                Control target = hovering.ActiveContextTarget();
                Control previous = pressTarget;
                _sameTargetTap = ReferenceEquals(target, previous);
                if (!_sameTargetTap)
                {
                    Context.Set("NextPressTarget", target);
                    (previous as IContext)?.OnContextRemoved("NextPressTarget");
                    (target as IContext)?.OnContextAdded("NextPressTarget");
                }
                if (target?.takesActiveControl != false) SetActiveControl(target);
            }

            Dispatch(Event(hovering, point, delta, button, 0), PointerPhase.Press);
        }

        // A release only counts on the control the press landed on, so dragging off a button cancels it.
        private static void SolveRelease(Vector2D<float> point, Vector2D<float> delta, int button, int tapCount)
        {
            // Ahead of the guards below: a drag ends wherever the pointer is, which is rarely still
            // over the control the press landed on.
            if (button == PointerEvent.leftButton && dragging != null)
            {
                EndDrag();
                return;
            }

            if (hovering == null) return;
            if (button == PointerEvent.leftButton && !ReferenceEquals(hovering.ActiveContextTarget(), pressTarget)) return;

            Dispatch(Event(hovering, point, delta, button, tapCount), PointerPhase.Release);

            if (button == PointerEvent.leftButton && tapCount >= 2 && _sameTargetTap)
                Dispatch(Event(hovering, point, delta, button, tapCount), PointerPhase.Tap);
        }

        // Which window the pointer is over, and what that means for focus. An overlap is not
        // resolvable — GLFW publishes no z-order — so an ambiguous answer changes nothing rather
        // than guessing, and only an unambiguous one moves the active window.
        private static unsafe void SolveDragWindow(RenderWindow source)
        {
            int count = WindowsAt(ScreenPoint(source), out RenderWindow single);

            SetMouseOverWindow(count == 1 ? single : null);

            if (count == 0) SetActiveWindow(null);
            else if (count == 1 && !ReferenceEquals(single, activeWindow))
            {
                single.os.Raise();
                SetActiveWindow(single);
            }
        }

        // How many windows hold the point, and which one when that is exactly one.
        private static unsafe int WindowsAt(Vector2D<float> screen, out RenderWindow single)
        {
            single = null;
            int count = 0;

            foreach (RenderWindow window in Engine.windows.Values)
            {
                // the preview sits under the pointer by definition, so it is never a drop target
                if (window.closeRequested || window.isGhost) continue;

                AGlfwWindow._glfw.GetWindowPos(window.os.handle, out int x, out int y);
                Extent2D size = window.os.windowSize;
                if (screen.X < x || screen.Y < y || screen.X >= x + size.Width || screen.Y >= y + size.Height)
                    continue;

                count++;
                single = window;
            }
            return count;
        }

        private static unsafe Vector2D<float> ScreenPoint(RenderWindow source)
        {
            AGlfwWindow._glfw.GetWindowPos(source.os.handle, out int sx, out int sy);
            return new Vector2D<float>(sx + source.mousePos.X, sy + source.mousePos.Y);
        }

        private static void SetMouseOverWindow(RenderWindow window)
        {
            if (ReferenceEquals(mouseOverWindow, window)) return;

            bool wasIn = mouseOverWindow != null;
            Context.Set("NextMouseOverWindow", window);

            if (wasIn) dragging?.DraggedOutOfWindow();
            if (window != null) dragging?.DraggedIntoWindow();
        }

        private static void SetActiveWindow(RenderWindow window)
        {
            if (ReferenceEquals(activeWindow, window)) return;
            Context.Set("NextActiveWindow", window);
        }

        // The control under the pointer mid-drag, in the target window's own design space. Found by
        // geometry rather than by hover: a captured pointer means no other window is ever told the
        // pointer is over it.
        private static unsafe Control HitFor(RenderWindow source, out Vector2D<float> local)
        {
            local = Vector2D<float>.Zero;

            RenderWindow target = mouseOverWindow;
            WindowRoot root = target?.uiNext?.uiRoot;
            if (root == null) return null;

            Vector2D<float> screen = ScreenPoint(source);
            AGlfwWindow._glfw.GetWindowPos(target.os.handle, out int tx, out int ty);

            local = root.ToDesignSpace(new Vector2D<float>(screen.X - tx, screen.Y - ty), target.os.windowSize);
            return HitTest(root, local, dragging);
        }

        // What the drag is over, and the offer to whatever that is. The hover's own walk, minus the
        // dragged subtree — it sits under the pointer by definition, so it would answer every time.
        // One control is the target, as one control is hovered; the events bubble from it.
        private static void CheckDrag(RenderWindow source)
        {
            if (dragging == null) return;

            Control target = HitFor(source, out Vector2D<float> point);
            _dragPoint = point;

            if (!ReferenceEquals(target, _dragTarget))
            {
                for (Control c = _dragTarget; c != null; c = c.parent as Control)
                    if (c.DraggingOverEnd(dragging)) break;

                _dragTarget = target;

                for (Control c = target; c != null; c = c.parent as Control)
                    if (c.DraggingOverStart(dragging, point)) break;
            }

            for (Control c = target; c != null; c = c.parent as Control)
                if (c.DraggingOver(dragging, point)) break;
        }

        // The window a control is drawn into — its root is some window's uiNext root. Null for a
        // subtree detached from every window.
        public static RenderWindow WindowOf(Control control)
        {
            Control root = RootOf(control);
            if (root == null) return null;

            foreach (RenderWindow window in Engine.windows.Values)
                if (ReferenceEquals(window.uiNext?.uiRoot, root))
                    return window;
            return null;
        }

        // Ends the drag: the target hears the drag leave, then that it was dropped on it. Telling the
        // claimant is still to come.
        public static void EndDrag()
        {
            Control target = _dragTarget;
            Control dragged = dragging;

            for (Control c = target; c != null; c = c.parent as Control)
                if (c.DraggingOverEnd(dragged)) break;

            target?.FinishDrag(dragged, _dragPoint);

            _dragTarget = null;
            SetMouseOverWindow(null);
            SetDragging(null);
        }

        // The wheel walks up from the hovered control until something consumes it.
        private static void SolveScroll(Vector2D<float> point, Vector2D<float> offset)
        {
            if (hovering == null) return;
            Dispatch(Event(hovering, point, offset, 0, 0), PointerPhase.Scroll);
        }

        // Also called with no click behind it, by anything that has to hold the active context before
        // the pointer ever reaches it.
        public static void SetActiveControl(Control control)
        {
            if (ReferenceEquals(activeControl, control)) return;

            Control previous = activeControl;
            Context.Set("NextActiveControl", control);
            (previous as IContext)?.OnContextRemoved("NextActiveControl");
            (control as IContext)?.OnContextAdded("NextActiveControl");
        }

        // Assigns the drag context and notifies both sides. Null ends the claim.
        public static void SetDragging(Control control)
        {
            if (ReferenceEquals(dragging, control)) return;

            Control previous = dragging;
            Context.Set("NextDragging", control);
            (previous as IContext)?.OnContextRemoved("NextDragging");
            (control as IContext)?.OnContextAdded("NextDragging");
        }

        // Drops a destroyed control out of every context holding it. Assigns directly — the control
        // is going away, so notifying it is the thing to avoid.
        public static void Forget(Control control)
        {
            if (ReferenceEquals(hovering, control)) hovering = null;
            if (ReferenceEquals(activeControl, control)) activeControl = null;
            if (ReferenceEquals(pressTarget, control)) pressTarget = null;
            if (ReferenceEquals(dragging, control)) dragging = null;
            if (ReferenceEquals(_dragTarget, control)) _dragTarget = null;
            Context.Forget(control);
        }

        // Deepest control whose clip and box hold the point. Children last to first, because depth
        // testing is off and the later sibling is the one drawn on top. skip takes a whole subtree
        // out of the answer, rejected at its root so nothing beneath it is reached either.
        public static Control HitTest(Control control, Vector2D<float> point, Control skip = null)
        {
            if (control.hidden || ReferenceEquals(control, skip)) return null;
            if (!control.arrange.subtreeBounds.Contains(point)) return null;

            for (int i = control.children.Count - 1; i >= 0; i--)
            {
                if (control.children[i] is not Control child || !child.hitTestable) continue;

                Control deeper = HitTest(child, point, skip);
                if (deeper != null) return deeper;
            }
            return HitsNode(control, point) ? control : null;
        }

        // The clip is inherited, so a point outside it rules the control out wherever it was arranged.
        // An axis-aligned box is exact while nothing rotates; a rotation would change only this.
        private static bool HitsNode(Control control, Vector2D<float> point)
        {
            ref ArrangeData a = ref control.arrange;
            return a.clip.Contains(point) && a.arranged.Contains(point);
        }

        private static void Dispatch(PointerEvent e, PointerPhase phase)
        {
            Control control = e.target;
            while (control != null)
            {
                bool consumed = phase switch
                {
                    PointerPhase.Enter => control.OnPointerEnter(e),
                    PointerPhase.Exit => control.OnPointerExit(e),
                    PointerPhase.Move => control.OnPointerMove(e),
                    PointerPhase.Press => control.OnPointerPress(e),
                    PointerPhase.Release => control.OnPointerRelease(e),
                    PointerPhase.Tap => control.OnPointerTap(e),
                    PointerPhase.Scroll => control.OnPointerScroll(e),
                    _ => false
                };
                if (consumed) return;
                control = control.parent as Control;
            }
        }

        private static PointerEvent Event(Control target, Vector2D<float> point, Vector2D<float> delta,
                                          int button, int tapCount) =>
            new PointerEvent
            {
                target = target,
                point = point,
                delta = delta,
                button = button,
                tapCount = tapCount
            };

        private static Control RootOf(Control control)
        {
            while (control?.parent is Control parent)
                control = parent;
            return control;
        }
        #endregion

        #region ---- dense order ----
        // Canonical dense order for both pools = DFS pre-order of the control tree, which is painter
        // order and layout-dependency order at once. UIElements is keyed by the entity's own handle,
        // VulkanControls by the draw row it allocated, so the walk is shared and the key is not.
        [A_XSDActionDependency("UI.NextElementOrder", "PoolSort")]
        public static IReadOnlyList<int> NextElementOrder(DataPool pool) => DFSOrder(pool, true);

        [A_XSDActionDependency("UI.NextControlOrder", "PoolSort")]
        public static IReadOnlyList<int> NextControlOrder(DataPool pool) => DFSOrder(pool, false);

        private static IReadOnlyList<int> DFSOrder(DataPool pool, bool elements)
        {
            int count = pool.Count;
            List<int> order = new List<int>(count);

            // Also what stops a control being collected twice: a run owns one dense row per glyph,
            // so the sweep below meets a detached run once per row it holds.
            HashSet<Control> collected = new HashSet<Control>();
            foreach (RenderWindow window in Engine.windows.Values)
            {
                WindowRoot root = window.uiNext?.uiRoot;
                if (root == null || !collected.Add(root)) continue;
                CollectDFS(root, order, elements);
            }

            // Detached subtrees are roots too, and no window draws them. They follow every window so
            // the window ranges stay contiguous from zero.
            for (int i = 0; i < count; i++)
            {
                if (pool.OwnerAt(i) is Control control && control.parent is not Control
                    && collected.Add(control))
                    CollectDFS(control, order, elements);
            }
            return order;
        }

        private static void CollectDFS(Control control, List<int> order, bool elements)
        {
            if (elements) order.Add(control.dataHandle.StableId);
            else foreach (DataHandle row in control.rows) order.Add(row.StableId);

            foreach (Entity child in control.children)
                if (child is Control childControl)
                    CollectDFS(childControl, order, elements);
        }
        #endregion

        #region ---- per-window instance ranges ----
        private static ulong _rangesStructural = ulong.MaxValue;
        private static ulong _rangesOrder = ulong.MaxValue;
        private static bool _rangesDirty = true;

        // A root swap moves no pool rows, so no pool version would report it.
        public static void InvalidateWindowRanges() => _rangesDirty = true;

        // Publishes each window module's slice of the shared draw pool. Dense order is DFS with the
        // windows in Engine.windows order, so a window's subtree is the contiguous run starting where
        // the previous window's ended. Runs at the frame edge, never per frame.
        public static void RefreshWindowRanges()
        {
            DataPool pool = Controls;
            if (!_rangesDirty && pool.StructuralVersion == _rangesStructural && pool.OrderVersion == _rangesOrder)
                return;

            _rangesStructural = pool.StructuralVersion;
            _rangesOrder = pool.OrderVersion;
            _rangesDirty = false;

            int first = 0;
            foreach (RenderWindow window in Engine.windows.Values)
            {
                UIEngineModule ui = window.uiNext;
                int count = ui.uiRoot == null ? 0 : CountSubtree(ui.uiRoot);
                Publish(ui, first, count);
                first += count;
            }
        }

        // The range rides in the recorded command buffer, so a module whose range moved has to
        // record again.
        private static void Publish(UIEngineModule ui, int first, int count)
        {
            if (ui.firstInstance == first && ui.instanceCount == count) return;

            ui.firstInstance = first;
            ui.instanceCount = count;

            if (ui.isDirty == null) return;
            for (int i = 0; i < ui.isDirty.Length; i++)
                ui.isDirty[i] = true;
        }

        // Draw rows, not controls — a text run contributes one per glyph.
        private static int CountSubtree(Control control)
        {
            int count = control.rows.Length;
            foreach (Entity child in control.children)
                if (child is Control childControl)
                    count += CountSubtree(childControl);
            return count;
        }
        #endregion

        [A_XSDActionDependency("UIEngine.Bootstrap", "Bootstrap")]
        public static bool Bootstrap()
        {
            Log.Info($"row sizes — ArrangeData {Unsafe.SizeOf<ArrangeData>()} B, " +
                     $"ControlGeometry {Unsafe.SizeOf<ControlGeometry>()} B, " +
                     $"VulkanControl {Unsafe.SizeOf<VulkanControl>()} B");

            BuildProbe(Engine.primary);
            return true;
        }

        // Landing 6a scaffolding: the probe document is the only thing on the new stack that XML can
        // build, because no subclass carries an [A_XSDType] until 6b. It exists to prove the parser,
        // the converters and the container base, and 6b deletes it with the document.
        private static void BuildProbe(RenderWindow window)
        {
            WindowRoot root = (WindowRoot)Control.ParseXML("next-probe");
            window.uiNext.uiRoot = root;

            // A delegate handler, which is how a ported subclass will reach the new scroll path.
            // Registered here rather than authored, because binding an event from XML is the one
            // thing 6a left open.
            Control bar = root.FindByName("bar");
            bar.RegisterOnScroll(e => { bar.colorHex = e.delta.Y > 0 ? "#FFD166" : "#8338EC"; return true; });
        }

    }
}
