using ArctisAurora.EngineWork.Rendering;

namespace ArctisAurora.Core.Filing
{
    // The OS folder dialog; null comes back when it is cancelled.
    public static class FolderPicker
    {
        public static bool isOpen => FileDialog.isOpen;

        public static void Pick(RenderWindow owner, string startFolder, Action<string> onPicked) =>
            FileDialog.Pick(owner, startFolder, true, null, onPicked);
    }
}
