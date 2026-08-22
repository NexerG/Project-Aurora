using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // One page of a TabView: a caption for the strip and the single control it shows.
    [A_XSDType("TabItem", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class TabItemControl : PanelControl
    {
        [A_XSDElementProperty("Header", "UI", "Caption shown on the tab strip.")]
        public string header = "Tab";

        // What a committed caption edit does. Null on a tab nobody can rename.
        public Action<string>? onRename;

        public TabItemControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }
    }
}
