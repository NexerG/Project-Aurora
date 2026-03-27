using ArctisAurora.Core.AssetRegistry;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.EngineWork
{
    [A_XSDType("Step", "Bootstrap")]
    public class BootstrapStep
    {
        [A_XSDElementProperty("Action", "Bootstrap")]
        public Action action { get; set; }
    }

    [A_XSDType("Phase", "Bootstrap")]
    public class BootstrapPhase
    {
        [A_XSDElementProperty("Name", "Bootstrap")]
        public string name { get; set; } = string.Empty;

        [A_XSDElementProperty("Step", "Bootstrap")]
        public List<BootstrapStep> steps { get; set; } = new();
    }

    [A_XSDType("BootstrapSequence", "Bootstrap")]
    internal static class Bootstrapper
    {
        [A_XSDElementProperty("Phase", "Bootstrap")]
        public static List<BootstrapPhase> phases { get; set; } = new();

        private static Dictionary<string, List<string>> _phases = new();  // phase name -> ordered step names
        private static Dictionary<string, MethodInfo> _actions = new();   // step name -> method

        public static void Load(string xmlPath)
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in generalAsm)
            {
                foreach (var type in asm.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        var attr = method.GetCustomAttribute<A_XSDActionDependencyAttribute>();
                        if (attr != null && attr.Category == "Bootstrap")
                            _actions[attr.Name] = method;
                    }
                }
            }

            XElement root = XElement.Load(xmlPath);
            XNamespace ns = root.GetDefaultNamespace();
            foreach (XElement phaseElem in root.Elements(ns + "Phase"))
            {
                string phaseName = phaseElem.Attribute("Name")?.Value ?? "Default";
                List<string> steps = new List<string>();
                foreach (XElement step in phaseElem.Elements(ns + "Step"))
                {
                    string action = step.Attribute("Action")?.Value;
                    if (action != null)
                        steps.Add(action);
                }
                _phases[phaseName] = steps;
            }
        }

        public static void RunPhase(string phaseName)
        {
            if (!_phases.TryGetValue(phaseName, out List<string> steps))
            {
                Console.WriteLine($"[Bootstrap] Phase '{phaseName}' not found.");
                return;
            }
            foreach (string stepName in steps)
            {
                if (!_actions.TryGetValue(stepName, out MethodInfo method))
                {
                    Console.WriteLine($"[Bootstrap] Action '{stepName}' not found — skipping.");
                    continue;
                }
                Console.WriteLine($"[Bootstrap] Running: {stepName}");
                method.Invoke(null, null);
            }
        }
    }
}