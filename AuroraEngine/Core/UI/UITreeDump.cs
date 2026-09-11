using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using System.Globalization;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // Writes every window's control tree, as laid out, to uitree.xml beside the executable.
    public static class UITreeDump
    {
        [A_XSDActionDependency("UI.DumpTree", "Input", "Writes every window's control tree, as laid out, to uitree.xml beside the executable")]
        public static void Dump()
        {
            XElement tree = new XElement("UITree");
            foreach (KeyValuePair<string, RenderWindow> window in Engine.windows)
            {
                WindowRoot root = window.Value.uiNext?.uiRoot;
                if (root == null) continue;

                tree.Add(new XElement("Window", new XAttribute("Key", window.Key), Node(root)));
            }
            tree.Save(Path.Combine(AppContext.BaseDirectory, "uitree.xml"));
        }

        private static XElement Node(Control control)
        {
            LayoutRect rect = control.arrangedRect;
            XElement node = new XElement("Control",
                new XAttribute("Type", control.GetType().Name),
                new XAttribute("Name", control.name),
                new XAttribute("X", Number(rect.x)),
                new XAttribute("Y", Number(rect.y)),
                new XAttribute("W", Number(rect.width)),
                new XAttribute("H", Number(rect.height)),
                new XAttribute("DesiredW", Number(control.DesiredSize.X)),
                new XAttribute("DesiredH", Number(control.DesiredSize.Y)),
                new XAttribute("Hidden", control.hidden));

            foreach (Entity child in control.children)
                if (child is Control childControl)
                    node.Add(Node(childControl));
            return node;
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
