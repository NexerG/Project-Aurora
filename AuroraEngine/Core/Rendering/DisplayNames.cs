using System.Runtime.InteropServices;
using Windows.Devices.Display;

namespace ArctisAurora.EngineWork.Rendering
{
    internal static class DisplayNames
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public uint cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string deviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string deviceString;
            public uint stateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string deviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string deviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice info, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode devMode);

        // The panel name Windows shows for the display whose top-left corner is at (x, y), or null.
        public static string At(int x, int y)
        {
            DisplayDevice adapter = new DisplayDevice { cb = (uint)Marshal.SizeOf<DisplayDevice>() };

            for (uint i = 0; EnumDisplayDevicesW(null, i, ref adapter, 0); i++)
            {
                adapter.cb = (uint)Marshal.SizeOf<DisplayDevice>();
                if ((adapter.stateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

                DevMode mode = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
                if (!EnumDisplaySettingsW(adapter.deviceName, ENUM_CURRENT_SETTINGS, ref mode)) continue;
                if (mode.dmPositionX != x || mode.dmPositionY != y) continue;

                return PanelName(adapter.deviceName);
            }
            return null;
        }

        // Walks from the adapter to its monitor child, whose device interface path names the panel.
        private static string PanelName(string adapterName)
        {
            DisplayDevice monitor = new DisplayDevice { cb = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevicesW(adapterName, 0, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME)) return null;

            try
            {
                DisplayMonitor panel = DisplayMonitor.FromInterfaceIdAsync(monitor.deviceId).AsTask().GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(panel?.DisplayName)) return panel.DisplayName;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Renderer] could not read the panel name for {adapterName} — {e.Message}");
            }

            string[] parts = monitor.deviceId.Split('#');
            return parts.Length > 1 ? parts[1] : null;
        }
    }
}
