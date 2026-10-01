using ArctisAurora.Core.Testing;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;

namespace ArctisAurora.Core.Filing
{
    // Text on the OS clipboard. A test run keeps it in process, so it never touches the user's.
    public static unsafe class ClipboardText
    {
        private static string? testText;

        public static string? Get()
        {
            if (TestRunner.active) return testText;
            if (Engine.primary == null) return null;

            return AGlfwWindow._glfw.GetClipboardString(Engine.primary.os.handle);
        }

        public static void Set(string text)
        {
            if (TestRunner.active)
            {
                testText = text;
                return;
            }
            if (Engine.primary == null) return;

            AGlfwWindow._glfw.SetClipboardString(Engine.primary.os.handle, text);
        }
    }
}
