using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Containers;
using ArctisAurora.Core.UISystem.Controls.Text.Editing;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    [A_XSDType("TextBlock", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class TextBlockControl : AbstractContainerControl
    {
        public TextBlockControl()
        {
            preferredWidth = 0;
            preferredHeight = 0;
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        public override Vector2D<float> Measure(Vector2D<float> availableSize)
        {
            LayoutRect inner = new LayoutRect(0, 0, availableSize.X, availableSize.Y).Shrink(padding);

            float cursorX = 0f;
            float lineHeight = 0f;
            float totalHeight = 0f;
            float maxWidth = 0f;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl child) continue;

                // Tell TextInputControls where they start on the line
                if (child is TextInputControl input)
                    input.firstLineOffset = cursorX;

                Vector2D<float> desired = child.Measure(new Vector2D<float>(inner.width, float.MaxValue));

                float childW = desired.X + child.margin.totalHorizontal;
                float childH = desired.Y + child.margin.totalVertical;

                // For non-wrapping children (buttons etc.), check if they fit
                if (child is not TextInputControl)
                {
                    if (cursorX + childW > inner.width && cursorX > 0)
                    {
                        // Line break
                        totalHeight += lineHeight;
                        if (cursorX > maxWidth) maxWidth = cursorX;
                        cursorX = 0f;
                        lineHeight = 0f;
                    }
                    cursorX += childW;
                    if (childH > lineHeight) lineHeight = childH;
                }
                else
                {
                    // TextInputControl handled its own wrapping.
                    // It may span multiple lines. We need its last line position.
                    TextInputControl inp = (TextInputControl)child;

                    // If it wrapped, the height includes all lines
                    if (childH > lineHeight) lineHeight = childH;

                    // Account for multi-line: total height absorbed all but the last line
                    float heightAboveLastLine = childH - inp.lastLineHeight;
                    if (heightAboveLastLine > 0)
                    {
                        totalHeight += heightAboveLastLine;
                        lineHeight = inp.lastLineHeight;
                    }

                    cursorX = inp.lastLineEndX;
                    if (inner.width > maxWidth) maxWidth = inner.width;
                }
            }

            // Last line
            totalHeight += lineHeight;
            if (cursorX > maxWidth) maxWidth = cursorX;

            float w = preferredWidth > 0 ? preferredWidth : maxWidth + padding.totalHorizontal;
            float h = preferredHeight > 0 ? preferredHeight : totalHeight + padding.totalVertical;

            DesiredSize = new Vector2D<float>(w, h);
            isMeasureDirty = false;
            return DesiredSize;
        }

        public override void Arrange(LayoutRect finalRect)
        {
            arrangedRect = finalRect;
            transform.SetWorldPosition(new Vector3D<float>(
                finalRect.x + finalRect.width / 2f,
                finalRect.y + finalRect.height / 2f,
                parent != null ? parent.transform.GetEntityPosition().Z + 0.001f : transform.GetEntityPosition().Z));
            transform.SetWorldScale(new Vector3D<float>(finalRect.width, finalRect.height, 1));

            ClipRect = parent is VulkanControl p
                ? (clipOutOfBounds ? LayoutRect.Intersect(finalRect, p.ClipRect) : p.ClipRect)
                : finalRect;

            LayoutRect inner = finalRect.Shrink(padding);
            float cursorX = 0f;
            float cursorY = inner.y;
            float lineHeight = 0f;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not VulkanControl child) continue;

                if (child is TextInputControl input)
                {
                    input.firstLineOffset = cursorX;

                    float cx = inner.x; // TextInput positions its own glyphs using firstLineOffset
                    float cy = cursorY;
                    child.Arrange(new LayoutRect(cx, cy, inner.width, child.DesiredSize.Y));

                    float heightAboveLastLine = child.DesiredSize.Y - input.lastLineHeight;
                    if (heightAboveLastLine > 0)
                    {
                        cursorY += heightAboveLastLine;
                        lineHeight = input.lastLineHeight;
                    }
                    else
                    {
                        if (child.DesiredSize.Y > lineHeight) lineHeight = child.DesiredSize.Y;
                    }
                    cursorX = input.lastLineEndX;
                }
                else
                {
                    float childW = child.DesiredSize.X + child.margin.totalHorizontal;
                    float childH = child.DesiredSize.Y + child.margin.totalVertical;

                    if (cursorX + childW > inner.width && cursorX > 0)
                    {
                        cursorY += lineHeight;
                        cursorX = 0f;
                        lineHeight = 0f;
                    }

                    float cx = inner.x + cursorX + child.margin.left;
                    float cy = cursorY + child.margin.top;
                    child.Arrange(new LayoutRect(cx, cy, child.DesiredSize.X, child.DesiredSize.Y));

                    cursorX += childW;
                    if (childH > lineHeight) lineHeight = childH;
                }
            }

            isArrangeDirty = false;
        }
    }
}
