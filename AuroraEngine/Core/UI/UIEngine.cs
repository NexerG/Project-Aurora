using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Modules;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CaretGeometry = ArctisAurora.Core.UISystem.Controls.Text.Document.CaretGeometry;

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

            // The pointer left this window, so nothing of ours is under it any more. Scoped to our
            // own tree, because every window polls and the contexts are global.
            if (!window.isInWindow)
            {
                if (RootOf(hovering) == root) SetHovering(null, point, delta);
                return;
            }

            SolveHover(point, delta, root);

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
                Control previous = pressTarget;
                _sameTargetTap = ReferenceEquals(hovering, previous);
                if (!_sameTargetTap)
                {
                    Context.Set("NextPressTarget", hovering);
                    (previous as IContext)?.OnContextRemoved("NextPressTarget");
                    (hovering as IContext)?.OnContextAdded("NextPressTarget");
                }
                SetActiveControl(hovering);
            }

            Dispatch(Event(hovering, point, delta, button, 0), PointerPhase.Press);
        }

        // A release only counts on the control the press landed on, so dragging off a button cancels it.
        private static void SolveRelease(Vector2D<float> point, Vector2D<float> delta, int button, int tapCount)
        {
            if (hovering == null) return;
            if (button == PointerEvent.leftButton && !ReferenceEquals(hovering, pressTarget)) return;

            Dispatch(Event(hovering, point, delta, button, tapCount), PointerPhase.Release);

            if (button == PointerEvent.leftButton && tapCount >= 2 && _sameTargetTap)
                Dispatch(Event(hovering, point, delta, button, tapCount), PointerPhase.Tap);
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

        // Drops a destroyed control out of every context holding it. Assigns directly — the control
        // is going away, so notifying it is the thing to avoid.
        public static void Forget(Control control)
        {
            if (ReferenceEquals(hovering, control)) hovering = null;
            if (ReferenceEquals(activeControl, control)) activeControl = null;
            if (ReferenceEquals(pressTarget, control)) pressTarget = null;
            Context.Forget(control);
        }

        // Deepest control whose clip and box hold the point. Children last to first, because depth
        // testing is off and the later sibling is the one drawn on top.
        public static Control HitTest(Control control, Vector2D<float> point)
        {
            if (control.hidden) return null;
            if (!control.arrange.subtreeBounds.Contains(point)) return null;

            for (int i = control.children.Count - 1; i >= 0; i--)
            {
                if (control.children[i] is not Control child) continue;

                Control deeper = HitTest(child, point);
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

            BuildScaffolding(Engine.primary);
            return true;
        }

        // Landing 2 scaffolding: nesting, padding, margin, alignment and a clipped overflow, so the
        // arrange pass has something to be wrong about. Built back to front, so pool allocation
        // order is nothing like DFS order and the resequence has to earn its keep. The port at
        // landing 6 removes it.
        private static void BuildScaffolding(RenderWindow window)
        {
            Control bar = new ProbeControl
            {
                name = "bar",
                preferredHeight = 48,
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Bottom,
                colorHex = "#06D6A0",
                cornerRadius = 6f
            };

            // Overlapping siblings. over is added last, so it draws on top and must take the hit.
            Control under = new ProbeControl
            {
                name = "under",
                preferredWidth = 200,
                preferredHeight = 120,
                horizontalAlignment = HorizontalAlignment.Center,
                verticalAlignment = VerticalAlignment.Center,
                colorHex = "#8338EC",
                cornerRadius = 8f
            };
            Control over = new ProbeControl
            {
                name = "over",
                preferredWidth = 120,
                preferredHeight = 200,
                horizontalAlignment = HorizontalAlignment.Center,
                verticalAlignment = VerticalAlignment.Center,
                colorHex = "#FB5607",
                cornerRadius = 8f
            };

            Control clipped = new Control
            {
                name = "clipped",
                preferredWidth = 200,
                preferredHeight = 140,
                horizontalAlignment = HorizontalAlignment.Right,
                verticalAlignment = VerticalAlignment.Top,
                clipOutOfBounds = true,
                colorHex = "#2A2F3A",
                cornerRadius = 8f
            };
            Control overflow = new Control
            {
                name = "overflow",
                preferredWidth = 320,
                preferredHeight = 260,
                colorHex = "#EF476F"
            };
            clipped.AddChild(overflow);

            Control card = new ProbeControl
            {
                name = "card",
                preferredWidth = 360,
                preferredHeight = 220,
                padding = new Thickness(16),
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top,
                colorHex = "#3AA6FF",
                cornerRadius = 16f,
                edgeColorHex = "#FFFFFF",
                edgeThickness = 2f
            };
            Control inner = new Control
            {
                name = "inner",
                padding = new Thickness(12),
                colorHex = "#12314A",
                cornerRadius = 8f
            };
            // Passes everything through, so hovering it lights card two levels up.
            Control leaf = new ProbeControl(consumes: false)
            {
                name = "leaf",
                preferredWidth = 120,
                preferredHeight = 60,
                colorHex = "#FFD166",
                cornerRadius = 4f
            };
            inner.AddChild(leaf);
            card.AddChild(inner);

            WindowRoot root = new WindowRoot { name = "root", padding = new Thickness(24) };
            root.AddChild(card);
            root.AddChild(clipped);
            root.AddChild(under);
            root.AddChild(over);
            root.AddChild(bar);
            root.AddChild(BuildParagraph());

            window.uiNext.uiRoot = root;
        }

        // Landing 4 scaffolding: one run wrapping at 360, styled by three spans so a line crosses
        // segments and runIndex has to map back to the right span.
        private static Control BuildParagraph()
        {
            const string body = "The quick brown fox jumps over the lazy dog, then wraps onto another line. ";
            const string emphasis = "Bold and amber";
            const string tail = ", and back to regular text that keeps on wrapping.";

            TextRunControl run = new TextRunControl
            {
                name = "paragraph",
                preferredWidth = 360,
                horizontalPosition = 0f,
                text = body + emphasis + tail,
                colorHex = "#1B2430"
            };
            run.SetSpans(
                new StyleSpan { count = body.Length, style = FontStyle.Regular },
                new StyleSpan { count = emphasis.Length, style = FontStyle.Bold, colorHex = "#FFD166" },
                new StyleSpan { style = FontStyle.Regular });

            return new ProbeDocumentControl(run)
            {
                name = "document",
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Center
            };
        }

        // Landing 3 scaffolding: makes hover and press visible, and consumes or does not on demand.
        // The port at landing 6 removes it.
        private class ProbeControl : Control
        {
            private readonly bool _consumes;
            private string _resting;

            public ProbeControl(bool consumes = true)
            {
                _consumes = consumes;
            }

            public override bool OnPointerEnter(PointerEvent e)
            {
                _resting ??= colorHex;
                colorHex = "#FFFFFF";
                return _consumes;
            }

            public override bool OnPointerExit(PointerEvent e)
            {
                colorHex = _resting;
                return _consumes;
            }

            public override bool OnPointerPress(PointerEvent e)
            {
                colorHex = "#111111";
                return _consumes;
            }

            public override bool OnPointerRelease(PointerEvent e)
            {
                colorHex = "#FFFFFF";
                return _consumes;
            }
        }

        // Landing 4 scaffolding: the smallest thing that can own a caret across a run, because the
        // caret belongs to the document and there is no document until landing 6 ports one. Holds
        // its two children outright — a plain Control takes one, and containers arrive with the port.
        private class ProbeDocumentControl : Control, IGlyphPressTarget
        {
            private readonly TextRunControl _run;
            private readonly NextCaretControl _caret;
            private int _offset;

            public ProbeDocumentControl(TextRunControl run)
            {
                alpha = 0f;
                _run = run;
                _caret = new NextCaretControl { name = "caret", colorHex = "#FF3B30" };
                Adopt(_run);
                Adopt(_caret);
            }

            private void Adopt(Control child)
            {
                children.Add(child);
                child.parent = this;
                MarkTreeOrderDirty();
                InvalidateLayout();
            }

            public void GlyphPressed(TextRunControl run, int index)
            {
                _offset = index;
                _caret.Focus();
                InvalidateArrange();
            }

            // The caret is drawn last, so it takes the hit inside its own two pixels; the document
            // owns the area either way and resolves the point against the run.
            public override bool OnPointerPress(PointerEvent e)
            {
                GlyphPressed(_run, _run.IndexAt(e.point));
                return true;
            }

            public override Vector2D<float> Measure(Vector2D<float> availableSize)
            {
                Vector2D<float> desired = _run.Measure(availableSize);
                _caret.Measure(availableSize);

                arrange.desired = desired;
                SetFlag(ArrangeFlags.MeasureDirty, false);
                return desired;
            }

            public override void Arrange(LayoutRect finalRect)
            {
                WriteArranged(finalRect);
                _run.Arrange(finalRect);

                CaretGeometry caret = _run.CaretAt(_offset);
                Vector2D<float> origin = _run.TextOrigin;
                _caret.Arrange(new LayoutRect(origin.X + caret.x, origin.Y + caret.top,
                    NextCaretControl.Width, caret.height));

                SetFlag(ArrangeFlags.ArrangeDirty, false);
            }

            public override void AddChild(Entity entity) =>
                throw new Exception("The scaffolding document takes its children in its constructor");
        }
    }
}
