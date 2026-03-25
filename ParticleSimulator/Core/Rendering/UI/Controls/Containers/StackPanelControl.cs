using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using Silk.NET.Maths;

namespace ArctisAurora.Core.Rendering.UI.Controls.Containers
{
    [A_XSDType("StackPanel", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class StackPanelControl : AbstractContainerControl
    {
        #region enums
        [A_XSDType("Orientation", "UI")]
        public enum Orientation
        {
            Horizontal,
            Vertical
        }
        #endregion

        #region properties
        // settings
        [A_XSDElementProperty("Orientation", "UI", "")]
        public Orientation orientation = Orientation.Vertical;
        
        [A_XSDElementProperty("Spacing", "UI", "Space between children in pixels.")]
        public float Spacing = 0f;
        #endregion

        public StackPanelControl()
        {
            preferredWidth = 0;
            preferredHeight = 0;
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            LayoutRect inner = new LayoutRect(0, 0, availableSize.X, availableSize.Y)
                .Shrink(padding);

            float totalMain = 0f;
            float maxCross = 0f;
            int childCount = 0;

            foreach (Entity e in children)
            {
                if (e is not VulkanControl child) continue;

                // Offer full cross-axis, unconstrained main-axis.
                Vector2D<float> offer = orientation == Orientation.Vertical
                    ? new Vector2D<float>(inner.width, float.MaxValue)
                    : new Vector2D<float>(float.MaxValue, inner.height);

                Vector2D<float> desired = child.Measure(offer);

                float childMain = orientation == Orientation.Vertical
                    ? desired.Y + child.margin.totalVertical
                    : desired.X + child.margin.totalHorizontal;
                float childCross = orientation == Orientation.Vertical
                    ? desired.X + child.margin.totalHorizontal
                    : desired.Y + child.margin.totalVertical;

                totalMain += childMain;
                maxCross = MathF.Max(maxCross, childCross);
                childCount++;
            }

            if (childCount > 1)
                totalMain += Spacing * (childCount - 1);

            float w = orientation == Orientation.Vertical
                ? maxCross + padding.totalHorizontal
                : totalMain + padding.totalHorizontal;
            float h = orientation == Orientation.Vertical
                ? totalMain + padding.totalVertical
                : maxCross + padding.totalVertical;

            // Only override measured size when preferredWidth/Height is explicitly set.
            if (preferredWidth > 0) w = preferredWidth;
            if (preferredHeight > 0) h = preferredHeight;

            DesiredSize = new Vector2D<float>(w, h);
            IsMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;

            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent.transform.GetEntityPosition().Z + 0.001f));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            LayoutRect inner = finalRect.Shrink(padding);
            float cursor = orientation == Orientation.Vertical ? inner.y : inner.x;
            bool first = true;

            foreach (Entity e in children)
            {
                if (e is not VulkanControl child) continue;

                if (!first) cursor += Spacing;
                first = false;

                if (orientation == Orientation.Vertical)
                {
                    // Main axis: Y.  Cross axis: X — driven by child.HorizontalAlignment.
                    float availCrossW = inner.width - child.margin.totalHorizontal;

                    float childW = child.horizontalAlignment == HorizontalAlignment.Stretch
                        ? availCrossW
                        : child.DesiredSize.X;

                    float childX = child.horizontalAlignment switch
                    {
                        HorizontalAlignment.Left => inner.x + child.margin.left,
                        HorizontalAlignment.Right => inner.x + child.margin.left + (availCrossW - childW),
                        HorizontalAlignment.Center => inner.x + child.margin.left + (availCrossW - childW) * 0.5f,
                        _ => inner.x + child.margin.left,  // Stretch
                    };

                    float childY = cursor + child.margin.top;
                    child.Arrange(new LayoutRect(childX, childY, childW, child.DesiredSize.Y));
                    cursor += child.DesiredSize.Y + child.margin.totalVertical;
                }
                else
                {
                    // Main axis: X.  Cross axis: Y — driven by child.VerticalAlignment.
                    float availCrossH = inner.height - child.margin.totalVertical;

                    float childH = child.verticalAlignment == VerticalAlignment.Stretch
                        ? availCrossH
                        : child.DesiredSize.Y;

                    float childY = child.verticalAlignment switch
                    {
                        VerticalAlignment.Top => inner.y + child.margin.top,
                        VerticalAlignment.Bottom => inner.y + child.margin.top + (availCrossH - childH),
                        VerticalAlignment.Center => inner.y + child.margin.top + (availCrossH - childH) * 0.5f,
                        _ => inner.y + child.margin.top,  // Stretch
                    };

                    float childX = cursor + child.margin.left;
                    child.Arrange(new LayoutRect(childX, childY, child.DesiredSize.X, childH));
                    cursor += child.DesiredSize.X + child.margin.totalHorizontal;
                }
            }

            isArrangeDirty = false;
        }

        /*private void MeasureChild(StackPanelLevelSettings level, VulkanControl child)
        {
            if (orientation == Orientation.Vertical)
            {
                switch(child.scalingMode)
                {
                    case ScalingMode.Stretch:
                        level.bounds.Y = Math.Max(level.bounds.Y, child.preferredHeight);
                        level.fillers++;
                        break;
                    case ScalingMode.None:
                        level.bounds.Y = Math.Max(level.bounds.Y, child.preferredHeight);
                        switch (level.widthScaling)
                        {
                            case StackPanelLevelSettings.LevelBounds.ScaleToContent:
                                level.bounds.X += child.preferredWidth + level.spacing;
                                break;
                            case StackPanelLevelSettings.LevelBounds.Fill:
                                level.bounds.X = width - (horizontalMargin * 2);
                                break;
                            case StackPanelLevelSettings.LevelBounds.HardScale:
                                level.bounds.X = level.width;
                                break;
                        }
                        break;
                    case ScalingMode.Uniform:
                        level.fillers++;
                        break;
                    case ScalingMode.Fill:
                        level.fillers++;
                        break;
                }

            }
            else
            {
                switch(child.scalingMode)
                {
                    case ScalingMode.Stretch:
                        level.bounds.X = Math.Max(level.bounds.X, child.preferredWidth);
                        level.fillers++;
                        break;
                    case ScalingMode.None:
                        level.bounds.X = Math.Max(level.bounds.X, child.preferredWidth);
                        switch (level.heightScaling)
                        {
                            case StackPanelLevelSettings.LevelBounds.ScaleToContent:
                                level.bounds.Y += child.preferredHeight + level.spacing;
                                break;
                            case StackPanelLevelSettings.LevelBounds.Fill:
                                level.bounds.Y = transform.scale.Y - (verticalMargin * 2);
                                break;
                            case StackPanelLevelSettings.LevelBounds.HardScale:
                                level.bounds.Y = level.height;
                                break;
                        }
                        break;
                    case ScalingMode.Uniform:
                        level.fillers++;
                        break;
                }
            }
        }*/

        /*public override void MeasureSelf()
        {
            //figure out how much space is left after fixed sizes
            Vector2D<float> availableSpace = new Vector2D<float>(width - (horizontalMargin * stackPanelLevelSettings.Count), height - (verticalMargin * stackPanelLevelSettings.Count));
            int fillCount = 0;
            // subtract non-fill levels from available space
            foreach (var level in stackPanelLevelSettings)
            {
                if(orientation == Orientation.Horizontal)
                {
                    if (level.widthScaling == StackPanelLevelSettings.LevelBounds.Fill)
                        fillCount++;
                    else
                        availableSpace.X -= level.bounds.X;
                }
                else
                {
                    if (level.heightScaling == StackPanelLevelSettings.LevelBounds.Fill)
                        fillCount++;
                    else
                        availableSpace.Y -= level.bounds.Y;
                }
            }
            // measure the fill children
            if (fillCount > 0)
            {
                if (orientation == Orientation.Horizontal)
                {
                    availableSpace.X /= fillCount;
                }
                else
                {
                    availableSpace.Y /= fillCount;
                }
                //apply available space to fill levels
                foreach (var level in stackPanelLevelSettings)
                {
                    if (orientation == Orientation.Horizontal)
                    {
                        if (level.widthScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.X = availableSpace.X;
                        if (level.heightScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.Y = availableSpace.Y;
                    }
                    else
                    {
                        if (level.heightScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.Y = availableSpace.Y;
                        if (level.widthScaling == StackPanelLevelSettings.LevelBounds.Fill)
                            level.bounds.X = availableSpace.X;
                    }
                }
            }
            if (orientation == Orientation.Horizontal)
            {
                foreach (var level in stackPanelLevelSettings)
                {
                    if (level.fillers > 0)
                    {
                        level.spaceLeft.X = level.bounds.X - level.spacing;
                        level.spaceLeft.Y = (int)(height - (int)level.bounds.Y - level.spacing * level.fillers - verticalMargin);
                        level.spaceLeft.Y /= level.fillers;
                    }
                }
            }
            else
            {
                foreach (var level in stackPanelLevelSettings)
                {
                    if(level.fillers > 0)
                    {
                        level.spaceLeft.Y = level.bounds.Y - level.spacing;
                        level.spaceLeft.X = (int)(width - (int)level.bounds.X - level.spacing * level.fillers - horizontalMargin);
                        level.spaceLeft.X /= level.fillers;
                    }
                }
            }
            
            foreach (var level in stackPanelLevelSettings)
            {
                foreach (var child in level.children)
                {
                    switch(child.scalingMode)
                    {
                        case ScalingMode.None:
                            child.SetControlScale(new Vector2D<float>(child.preferredWidth, child.preferredHeight));
                            break;
                        case ScalingMode.Stretch:
                            if (orientation == Orientation.Horizontal)
                            {
                                child.SetControlScale(new Vector2D<float>(child.preferredWidth, level.spaceLeft.Y));
                            }
                            else
                            {
                                child.SetControlScale(new Vector2D<float>(level.spaceLeft.X, child.preferredHeight));
                            }
                            break;
                        case ScalingMode.Fill:
                            child.SetControlScale(level.spaceLeft);
                            break;
                        case ScalingMode.Uniform:
                            child.SetControlScale(level.spaceLeft);
                            break;
                    }
                }
            }


            // calculate each level's center position based on bounds and justification
            for (int i = 0; i < stackPanelLevelSettings.Count; i++)
            {
                Vector2D<float> pos = new Vector2D<float>(0, 0);
                if(orientation == Orientation.Horizontal)
                {
                    switch (stackPanelLevelSettings[i].verticalAlignment)
                    {
                        case StackPanelLevelSettings.VerticalAlignment.Center:
                            pos.Y = transform.scale.Y / 2;
                            break;
                        case StackPanelLevelSettings.VerticalAlignment.Top:
                            pos.Y = verticalMargin + (stackPanelLevelSettings[i].bounds.Y / 2);
                            break;
                        case StackPanelLevelSettings.VerticalAlignment.Bottom:
                            pos.Y = transform.scale.Y - (stackPanelLevelSettings[i].bounds.Y / 2) - verticalMargin;
                            break;
                    }
                    for(int k = i - 1; k >= 0; k--)
                    {
                        pos.X += stackPanelLevelSettings[k].bounds.X + stackPanelLevelSettings[k].spacing;
                    }
                    pos.X += horizontalMargin + stackPanelLevelSettings[i].bounds.X / 2;
                }
                else
                {
                    switch (stackPanelLevelSettings[i].horizontalAlignment)
                    {
                        case StackPanelLevelSettings.HorizontalAlignment.Center:
                            pos.X = transform.scale.X / 2;
                            break;
                        case StackPanelLevelSettings.HorizontalAlignment.Left:
                            pos.X = horizontalMargin + (stackPanelLevelSettings[i].bounds.X / 2);
                            break;
                        case StackPanelLevelSettings.HorizontalAlignment.Right:
                            pos.X = transform.scale.X - (stackPanelLevelSettings[i].bounds.X / 2) - horizontalMargin;
                            break;
                    }
                    for (int k = i - 1; k >= 0; k--)
                    {
                        pos.Y += stackPanelLevelSettings[k].bounds.Y + stackPanelLevelSettings[k].spacing;
                    }
                    pos.Y += verticalMargin + stackPanelLevelSettings[i].bounds.Y / 2;
                }
                stackPanelLevelSettings[i].position = pos;
            }

            // calculate starting nextPosition for each level
            for (int i = 0; i < stackPanelLevelSettings.Count; i++)
            {
                Vector3D<float> startPosRelative = new Vector3D<float>();
                switch (stackPanelLevelSettings[i].horizontalAlignment)
                {
                    case StackPanelLevelSettings.HorizontalAlignment.Center:
                        startPosRelative.X = startPosRelative.X - (stackPanelLevelSettings[i].bounds.X / 2);
                        break;
                    case StackPanelLevelSettings.HorizontalAlignment.Right:
                        startPosRelative.X = startPosRelative.X - (stackPanelLevelSettings[i].bounds.X / 2);
                        break;
                    case StackPanelLevelSettings.HorizontalAlignment.Left:
                        startPosRelative.X = startPosRelative.X - (stackPanelLevelSettings[i].bounds.X / 2);
                        break;
                }

                switch (stackPanelLevelSettings[i].verticalAlignment)
                {
                    case StackPanelLevelSettings.VerticalAlignment.Top:
                        startPosRelative.Y = startPosRelative.Y - (stackPanelLevelSettings[i].bounds.Y / 2);
                        break;
                    case StackPanelLevelSettings.VerticalAlignment.Center:
                        startPosRelative.Y += 0;
                        break;
                    case StackPanelLevelSettings.VerticalAlignment.Bottom:
                        startPosRelative.Y = startPosRelative.Y + (stackPanelLevelSettings[i].bounds.Y / 2);
                        break;
                }

                stackPanelLevelSettings[i].nextPosition = startPosRelative;
            }
        }*/

        /*private Vector3D<float> CalcPos(VulkanControl control)
        {
            float z = transform.position.Z;
            Vector3D<float> halfscale = new Vector3D<float>(control.width, control.height, 0) / 2;
            Vector3D<float> pos = new Vector3D<float>(stackPanelLevelSettings[control.stackIndex].position.X, stackPanelLevelSettings[control.stackIndex].position.Y, 0);
            if (orientation == Orientation.Horizontal)
            {
                // vertical alignment
                if (stackPanelLevelSettings[control.stackIndex].verticalAlignment == StackPanelLevelSettings.VerticalAlignment.Bottom)
                {
                    pos = pos + stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.Y = pos.Y - halfscale.Y;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.Y = stackPanelLevelSettings[control.stackIndex].nextPosition.Y - halfscale.Y * 2 - stackPanelLevelSettings[control.stackIndex].spacing;
                }
                else
                {
                    pos = pos + stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.Y = pos.Y + halfscale.Y;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.Y = stackPanelLevelSettings[control.stackIndex].nextPosition.Y + halfscale.Y * 2 + stackPanelLevelSettings[control.stackIndex].spacing;
                }
                // horizontal alignment
                if(stackPanelLevelSettings[control.stackIndex].horizontalAlignment == StackPanelLevelSettings.HorizontalAlignment.Right)
                {
                    pos.X = pos.X - halfscale.X;
                }
                else if(stackPanelLevelSettings[control.stackIndex].horizontalAlignment == StackPanelLevelSettings.HorizontalAlignment.Left)
                {
                    pos.X = pos.X + halfscale.X;
                }
            }
            else
            {
                if(stackPanelLevelSettings[control.stackIndex].horizontalAlignment == StackPanelLevelSettings.HorizontalAlignment.Right)
                {
                    pos = pos - stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.X = pos.X - halfscale.X;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.X = stackPanelLevelSettings[control.stackIndex].nextPosition.X - halfscale.X * 2 + stackPanelLevelSettings[control.stackIndex].spacing;
                }
                else
                {
                    pos = pos + stackPanelLevelSettings[control.stackIndex].nextPosition;
                    pos.X = pos.X + halfscale.X;
                    stackPanelLevelSettings[control.stackIndex].nextPosition.X = stackPanelLevelSettings[control.stackIndex].nextPosition.X + halfscale.X * 2 + stackPanelLevelSettings[control.stackIndex].spacing;
                }
                // vertical alignment
                if (stackPanelLevelSettings[control.stackIndex].verticalAlignment == StackPanelLevelSettings.VerticalAlignment.Bottom)
                {
                    pos.Y = pos.Y - halfscale.Y;
                }
                else if (stackPanelLevelSettings[control.stackIndex].verticalAlignment == StackPanelLevelSettings.VerticalAlignment.Top)
                {
                    pos.Y = pos.Y + halfscale.Y;
                }
            }
            pos.Z = z;

            return pos;
        }*/
    }
}