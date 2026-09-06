using ArctisAurora.Core.Data;
using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Registry;
using System.Runtime.CompilerServices;

namespace ArctisAurora.Core.UI
{
    // Entry point of the UI, on the main thread. Poll() and PollLayout() arrive at landings 2 and 3.
    public static class UIEngine
    {
        private static readonly LogChannel Log = LogChannel.For("UIEngine");

        private static DataPool _elements;
        private static DataPool _controls;

        public static DataPool Elements => _elements ??= DataManager.Get("UIElements");
        public static DataPool Controls => _controls ??= DataManager.Get("VulkanControls");

        // landing 1 scaffolding — the arrange pass replaces it
        internal static Control smokePanel;

        [A_XSDActionDependency("UIEngine.Bootstrap", "Bootstrap")]
        public static bool Bootstrap()
        {
            Log.Info($"row sizes — ArrangeData {Unsafe.SizeOf<ArrangeData>()} B, " +
                     $"ControlGeometry {Unsafe.SizeOf<ControlGeometry>()} B, " +
                     $"VulkanControl {Unsafe.SizeOf<VulkanControl>()} B");

            smokePanel = new Control
            {
                name = "smoke",
                colorHex = "#3AA6FF",
                cornerRadius = 16f,
                edgeColorHex = "#FFFFFF",
                edgeThickness = 2f
            };
            smokePanel.SetArranged(new LayoutRect(80, 80, 320, 180));
            return true;
        }
    }
}
