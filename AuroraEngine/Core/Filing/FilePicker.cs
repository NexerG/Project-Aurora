using ArctisAurora.EngineWork.Rendering;

namespace ArctisAurora.Core.Filing
{
    // The OS file-open dialog, limited to the filters given as (name, "*.png;*.jpg"); null comes back when it is cancelled.
    public static class FilePicker
    {
        public static bool isOpen => FileDialog.isOpen;

        public static void Pick(RenderWindow owner, string startFolder, (string name, string spec)[] filters, Action<string> onPicked) =>
            FileDialog.Pick(owner, startFolder, false, filters, onPicked);
    }
}
