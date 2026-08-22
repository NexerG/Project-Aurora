using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Interactable;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // Two panes with a grip between them. A StackPanel in every respect — it is its own type so that
    // collapsing a split can never reach authored chrome.
    [A_XSDType("SplitView", "UI", AllowedChildren = typeof(IXMLChild_UI))]
    public class SplitViewControl : StackPanelControl
    {
        public enum SplitEdge
        {
            Left, Right, Top, Bottom
        }

        // grip thickness and the floor a dragged pane stops at
        private const int gripThickness = 5;
        private const int paneMinimum = 120;

        public SplitViewControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // Puts a split where the source sits, the source on one side of the grip and a new view on
        // the other.
        public static TabViewControl Split(TabViewControl source, SplitEdge edge)
        {
            if (source.parent is not VulkanControl host) return null;

            bool vertical = edge is SplitEdge.Top or SplitEdge.Bottom;
            bool sourceFirst = edge is SplitEdge.Right or SplitEdge.Bottom;
            int slot = host.children.IndexOf(source);

            float main = vertical ? source.arrangedRect.height : source.arrangedRect.width;
            int half = (int)MathF.Max(paneMinimum, (main - gripThickness) * 0.5f);

            SplitViewControl split = new SplitViewControl
            {
                orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                widthStar = source.widthStar,
                heightStar = source.heightStar,
                preferredWidth = source.preferredWidth,
                preferredHeight = source.preferredHeight,
                minWidth = source.minWidth,
                minHeight = source.minHeight
            };

            TabViewControl fresh = NewPane(source);
            VulkanControl first = sourceFirst ? source : fresh;
            VulkanControl second = sourceFirst ? fresh : source;

            // the source leaves the host before the split arrives, so a single-child host is never
            // asked to hold both at once
            first.SetParent(split);
            split.AddChild(NewGrip(vertical));
            second.SetParent(split);
            split.SetParent(host);

            SizePane(first, vertical, half);
            SizePane(second, vertical, 0);

            host.children.Remove(split);
            host.children.Insert(slot, split);
            split.MarkTreeOrderDirty();
            host.InvalidateLayout();

            return fresh;
        }

        // A pane with nothing left in it takes the split down with it, and its neighbour moves up
        // into the split's slot.
        public static void Collapse(SplitViewControl split, VulkanControl leaving)
        {
            if (split.parent is not VulkanControl host) return;

            VulkanControl survivor = null;
            foreach (Entity e in split.children)
                if (e is VulkanControl pane && pane is not SplitterControl && !ReferenceEquals(pane, leaving))
                {
                    survivor = pane;
                    break;
                }

            int slot = host.children.IndexOf(split);
            leaving.Destroy();

            if (survivor != null)
            {
                survivor.widthStar = split.widthStar;
                survivor.heightStar = split.heightStar;
                survivor.preferredWidth = split.preferredWidth;
                survivor.preferredHeight = split.preferredHeight;
                survivor.minWidth = split.minWidth;
                survivor.minHeight = split.minHeight;

                survivor.SetParent(host);
                host.children.Remove(survivor);
                host.children.Insert(slot, survivor);
            }

            split.MarkTreeOrderDirty();
            split.Destroy();
            host.InvalidateLayout();
        }

        // The new pane is the source's kind and chrome with none of its contents.
        private static TabViewControl NewPane(TabViewControl source)
        {
            TabViewControl pane = source.NewOfSameKind();

            pane.name = source.name;
            pane.tabHeight = source.tabHeight;
            pane.tabWidth = source.tabWidth;
            pane.tabColorHex = source.tabColorHex;
            pane.activeTabColorHex = source.activeTabColorHex;
            pane.tabHoverColorHex = source.tabHoverColorHex;
            pane.tearOffDocument = source.tearOffDocument;
            pane.tabContextMenu = source.tabContextMenu;
            pane.contextMenus = source.contextMenus;
            pane.controlColorHex = source.controlColorHex;

            return pane;
        }

        // Only the main axis is pinned; the cross axis stretches, and a thickness on it would leave
        // the grip a five pixel square.
        private static SplitterControl NewGrip(bool vertical)
        {
            SplitterControl grip = new SplitterControl();
            if (vertical) grip.preferredHeight = gripThickness;
            else grip.preferredWidth = gripThickness;
            return grip;
        }

        // The pane before the grip carries a size and the one after it carries the star, which is the
        // shape SplitterControl resizes.
        private static void SizePane(VulkanControl pane, bool vertical, int fixedMain)
        {
            pane.widthStar = 0f;
            pane.heightStar = 0f;
            pane.preferredWidth = 0;
            pane.preferredHeight = 0;

            if (vertical) pane.minHeight = paneMinimum;
            else pane.minWidth = paneMinimum;

            if (fixedMain > 0)
            {
                if (vertical) pane.preferredHeight = fixedMain;
                else pane.preferredWidth = fixedMain;
            }
            else if (vertical) pane.heightStar = 1f;
            else pane.widthStar = 1f;
        }
    }
}
