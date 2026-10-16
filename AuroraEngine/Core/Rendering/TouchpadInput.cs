using ArctisAurora.Core.Diagnostics;
using System.Runtime.InteropServices;

namespace ArctisAurora.EngineWork.Rendering
{
    // Precision touchpad fingers from raw HID input, for when a scroll gesture ends.
    internal static unsafe class TouchpadInput
    {
        private static readonly LogChannel Log = LogChannel.For("Touchpad");

        // one touchpad's parsed report layout
        private sealed class Device
        {
            public IntPtr preparsed;
            public ushort[] fingers = Array.Empty<ushort>();
            public ushort countCollection;
            public bool hasCount;
        }

        private static readonly Dictionary<IntPtr, Device?> devices = new Dictionary<IntPtr, Device?>();
        private static bool registered;

        // the frame being read: contacts still to come and how many of them touch
        private static int expected;
        private static int touching;

        // fingers on the pad
        private static bool down;

        // Asks for touchpad reports, following keyboard focus.
        public static void Register()
        {
            if (registered) return;
            registered = true;
            RawInputDevice device = new RawInputDevice { usagePage = digitizerPage, usage = touchpadUsage };
            if (!RegisterRawInputDevices(&device, 1, (uint)sizeof(RawInputDevice)))
                Log.Warn($"touchpad raw input not registered: {Marshal.GetLastWin32Error()}");
        }

        // Reads one WM_INPUT.
        public static void Handle(IntPtr rawInput)
        {
            uint size = 0;
            uint header = (uint)sizeof(RawInputHeader);
            GetRawInputData(rawInput, rawInputCommand, null, ref size, header);
            if (size == 0) return;
            byte* buffer = stackalloc byte[(int)size];
            if (GetRawInputData(rawInput, rawInputCommand, buffer, ref size, header) != size) return;

            RawInputHeader* head = (RawInputHeader*)buffer;
            if (head->type != typeHid || Find(head->device) is not Device device) return;

            RawHid* hid = (RawHid*)(buffer + header);
            byte* report = buffer + header + sizeof(RawHid);
            for (uint r = 0; r < hid->count; r++, report += hid->sizeHid)
                Read(device, report, hid->sizeHid);
        }

        // One report: a contact count opens a frame, each finger slot fills it until the count is met.
        private static void Read(Device device, byte* report, uint length)
        {
            if (device.hasCount && HidP_GetUsageValue(inputReport, digitizerPage, device.countCollection, contactCountUsage, out uint count, device.preparsed, report, length) == hidSuccess && count > 0)
            {
                expected = (int)count;
                touching = 0;
            }

            ushort* usages = stackalloc ushort[16];
            foreach (ushort finger in device.fingers)
            {
                if (expected <= 0) break;
                uint found = 16;
                bool tip = false;
                if (HidP_GetUsages(inputReport, digitizerPage, finger, usages, ref found, device.preparsed, report, length) == hidSuccess)
                    for (int i = 0; i < found; i++)
                        tip |= usages[i] == tipSwitchUsage;
                if (tip) touching++;
                if (--expected > 0) continue;

                bool was = down;
                down = touching > 0;
                if (was != down)
                    InputHandler.Post(new RawInputEvent { kind = InputEventKind.ScrollContact, action = down ? RawAction.Down : RawAction.Up, timestamp = Engine.totalTime });
            }
        }

        // The device's finger collections, or null when it is not a touchpad with contact IDs.
        private static Device? Find(IntPtr handle)
        {
            if (devices.TryGetValue(handle, out Device? known)) return known;
            devices[handle] = null;

            uint size = 0;
            GetRawInputDeviceInfoW(handle, preparsedDataCommand, null, ref size);
            if (size == 0) return null;
            IntPtr preparsed = (IntPtr)NativeMemory.Alloc(size);
            if (GetRawInputDeviceInfoW(handle, preparsedDataCommand, (void*)preparsed, ref size) == uint.MaxValue || HidP_GetCaps(preparsed, out HidCaps caps) != hidSuccess)
            {
                NativeMemory.Free((void*)preparsed);
                return null;
            }

            ushort length = caps.inputValueCaps;
            HidValueCaps[] values = new HidValueCaps[length];
            fixed (HidValueCaps* first = values)
                if (HidP_GetValueCaps(inputReport, first, ref length, preparsed) != hidSuccess) length = 0;

            Device device = new Device { preparsed = preparsed };
            SortedSet<ushort> fingers = new SortedSet<ushort>();
            for (int i = 0; i < length; i++)
            {
                if (values[i].usagePage != digitizerPage) continue;
                if (values[i].usage == contactIdUsage) fingers.Add(values[i].linkCollection);
                else if (values[i].usage == contactCountUsage)
                {
                    device.countCollection = values[i].linkCollection;
                    device.hasCount = true;
                }
            }
            device.fingers = fingers.ToArray();
            if (device.fingers.Length == 0) return null;
            devices[handle] = device;
            return device;
        }

        #region ---- interop ----
        // HID digitizer page, touchpad, contact ID, contact count, tip switch
        private const ushort digitizerPage = 0x0D;
        private const ushort touchpadUsage = 0x05;
        private const ushort contactIdUsage = 0x51;
        private const ushort contactCountUsage = 0x54;
        private const ushort tipSwitchUsage = 0x42;

        // RID_INPUT, RIDI_PREPARSEDDATA, RIM_TYPEHID, HidP_Input, HIDP_STATUS_SUCCESS
        private const uint rawInputCommand = 0x10000003;
        private const uint preparsedDataCommand = 0x20000005;
        private const uint typeHid = 2;
        private const int inputReport = 0;
        private const int hidSuccess = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort usagePage;
            public ushort usage;
            public uint flags;
            public IntPtr target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint type;
            public uint size;
            public IntPtr device;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawHid
        {
            public uint sizeHid;
            public uint count;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct HidCaps
        {
            [FieldOffset(48)] public ushort inputValueCaps;
        }

        [StructLayout(LayoutKind.Explicit, Size = 72)]
        private struct HidValueCaps
        {
            [FieldOffset(0)] public ushort usagePage;
            [FieldOffset(6)] public ushort linkCollection;
            [FieldOffset(56)] public ushort usage;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr rawInput, uint command, void* data, ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, void* data, ref uint size);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsed, out HidCaps caps);

        [DllImport("hid.dll")]
        private static extern int HidP_GetValueCaps(int reportType, HidValueCaps* caps, ref ushort length, IntPtr preparsed);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsed, byte* report, uint length);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort* usages, ref uint length, IntPtr preparsed, byte* report, uint reportLength);
        #endregion
    }
}
