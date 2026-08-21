using ArctisAurora.Core.Registry;
using ArctisAurora.Core.UISystem.Actions;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.EngineWork
{
    [A_XSDType("Step", "Shutdown")]
    public class ShutdownStep
    {
        [A_XSDElementProperty("Action", "Shutdown")]
        public Action? action { get; set; }
    }

    [A_XSDType("Phase", "Shutdown")]
    public class ShutdownPhase
    {
        [A_XSDElementProperty("Name", "Shutdown")]
        public string name { get; set; } = string.Empty;

        [A_XSDElementProperty("Step", "Shutdown")]
        public List<ShutdownStep> steps { get; set; } = new();
    }

    // The bootstrap sequence run backwards: phases of ordered steps, declared in Shutdown.xml and
    // resolved to methods by name. Two phases, because they answer different questions — Request may
    // refuse and is where anything that asks the user lives, Commit is past the point of no return.
    [A_XSDType("ShutdownSequence", "Shutdown")]
    internal static class Shutdown
    {
        [A_XSDElementProperty("Phase", "Shutdown")]
        public static List<ShutdownPhase> phases { get; set; } = new();

        public const string requestPhase = "Request";
        public const string commitPhase = "Commit";

        private static Dictionary<string, List<string>> _phases = new();  // phase name -> ordered step names
        private static Dictionary<string, MethodInfo> _actions = new();   // step name -> method

        // True from the first Request until the process goes, so a step can tell a shutdown that is
        // under way from one that has not started.
        public static bool isClosing { get; private set; }

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
                        if (attr != null && attr.Category == "Shutdown")
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

        // A fresh attempt, from a user gesture. Anything a previous attempt remembered is dropped.
        public static void Request()
        {
            NoteActions.ForgetDiscarded();
            Run();
        }

        // Re-enters the sequence after a step that returned false went off to ask the user something.
        // The step that refused is re-run and answers differently this time.
        public static void Resume() => Run();

        private static void Run()
        {
            isClosing = true;

            if (!RunPhase(requestPhase))
            {
                isClosing = false;
                return;
            }

            // Nothing here may refuse — a failed step is logged and the rest of its phase is skipped,
            // but the application still goes, or one broken handler makes it unquittable.
            RunPhase(commitPhase);

            Engine.CloseWindow(Engine.primary);
        }

        // A step that reports failure halts its phase — nothing after it runs.
        public static bool RunPhase(string phaseName)
        {
            if (!_phases.TryGetValue(phaseName, out List<string> steps))
            {
                Console.WriteLine($"[Shutdown] Phase '{phaseName}' not found.");
                return false;
            }
            foreach (string stepName in steps)
            {
                if (!_actions.TryGetValue(stepName, out MethodInfo method))
                {
                    Console.WriteLine($"[Shutdown] Action '{stepName}' not found — skipping.");
                    continue;
                }
                Console.WriteLine($"[Shutdown] Running: {stepName}");
                if (method.Invoke(null, null) is false)
                {
                    Console.WriteLine($"[Shutdown] Step '{stepName}' reported failure — phase '{phaseName}' halted.");
                    return false;
                }
            }
            return true;
        }
    }
}
