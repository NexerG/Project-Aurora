using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Containers
{
    // Where a window's panes go. The document declares what fills it on a first run; the saved
    // session fills it on every run after that.
    [A_XSDType("Workspace", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class WorkspaceControl : PanelControl
    {
        [A_XSDElementProperty("Default", "UI", "UI document built into this workspace when the session has nothing for its window.")]
        public string defaultDocument = "";

        [A_XSDElementProperty("Pane", "UI", "UI document holding one empty pane, which a restored session splits to rebuild its arrangement.")]
        public string paneDocument = "";

        public WorkspaceControl()
        {
            maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
        }

        // The authored arrangement, for a window the session has nothing to say about.
        public void LoadDefault()
        {
            if (string.IsNullOrEmpty(defaultDocument)) return;

            AddChild(ParseXML(defaultDocument));
        }

        // One empty pane carrying this workspace's authored chrome. A restored arrangement is built
        // by splitting it, and SplitViewControl copies that chrome onto every pane it makes.
        public TabViewControl LoadPane()
        {
            if (string.IsNullOrEmpty(paneDocument)) return null;
            if (ParseXML(paneDocument) is not TabViewControl pane) return null;

            AddChild(pane);
            return pane;
        }

        // The workspace in a window's tree, or null for a window that declares none.
        public static WorkspaceControl In(VulkanControl control)
        {
            if (control == null) return null;
            if (control is WorkspaceControl workspace) return workspace;

            foreach (Entity child in control.children)
                if (child is VulkanControl childControl && In(childControl) is WorkspaceControl found)
                    return found;

            return null;
        }
    }
}
