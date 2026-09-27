using ArctisAurora.Core.UI;
using ArctisAurora.EngineWork;
using System.Runtime.CompilerServices;

namespace ArctisAurora.Core.Testing
{
    // What a test is handed: the fixture it shows and the checks it records.
    public sealed class TestContext
    {
        internal readonly List<(string message, string file, int line)> failures = new();

        // Shows content as the primary window's whole tree.
        public WindowRoot Show(Control content)
        {
            WindowRoot root = new WindowRoot();
            root.AddChild(content);
            Engine.primary.ui.uiRoot = root;
            return root;
        }

        // Records a failure when condition is false.
        public void Check(bool condition, string what,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (!condition) failures.Add((what, Path.GetFileName(file), line));
        }
    }
}
