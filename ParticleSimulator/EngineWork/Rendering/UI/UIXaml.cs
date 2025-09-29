using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    internal class UIXaml
    {
        private static readonly Dictionary<string,Type> ControlMap = BuildControlMap();
        /*new Dictionary<string, Type>()
    {
        { "VulkanControl", typeof(Controls.VulkanControl) },
        { "Button", typeof(Controls.Interactable.ButtonControl) },
        { "Panel", typeof(Controls.PanelControl) },
    };*/


        public static void ParseXAML(string xaml)
        {
            string path = Paths.UIXAML + "\\" + xaml;

            XDocument doc = XDocument.Load(path);
            XElement root = doc.Root;
            VulkanControl topControl = new VulkanControl();
            RecursiveParse(root, topControl);
            //VulkanControl control = CreateControlFromXML(root);
            // Now 'control' is the root control created from the XML
        }

        private static void RecursiveParse(XElement root, VulkanControl topControl)
        {
            Console.WriteLine($"Element: {root.Name}");
            foreach (var element in root.Elements())
            {
                if(!ControlMap.TryGetValue(element.Name.LocalName, out var controlType))
                    throw new Exception($"Unknown control type: {element.Name}");
                topControl.AddChild((VulkanControl)Activator.CreateInstance(controlType));

                RecursiveParse(element, topControl.child);
            }
        }

        private static Dictionary<string, Type> BuildControlMap()
        {
            var asm = typeof(VulkanControl).Assembly;
            return asm.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(VulkanControl).IsAssignableFrom(t) && t.GetCustomAttribute<A_VulkanControlAttribute>() != null)
                    .Select(t => new
                    {
                        Type = t,
                        Tag = t.GetCustomAttribute<A_VulkanControlAttribute>()?.Name ?? t.Name
                    })
                    .ToDictionary(x => x.Tag, x => x.Type);
        }

        private VulkanControl CreateControlFromXML(XElement element)
        {
            if (!ControlMap.TryGetValue(element.Name.LocalName, out var controlType))
                throw new Exception($"Unknown control type: {element.Name}");

            // create control
            var control = (VulkanControl)Activator.CreateInstance(controlType)!;

            // set properties from attributes
            foreach (var attr in element.Attributes())
            {
                var prop = controlType.GetProperty(attr.Name.LocalName);
                if (prop != null && prop.CanWrite)
                {
                    object value = Convert.ChangeType(attr.Value, prop.PropertyType);
                    prop.SetValue(control, value);
                }
            }

            // handle children
            /*foreach (var child in element.Elements())
            {
                if (control is IContainer container) // e.g. StackPanel, Dock, Grid
                {
                    var childControl = CreateControlFromXml(child);
                    container.AddChild(childControl);
                }
            }*/

            return control;
        }
    }
}
