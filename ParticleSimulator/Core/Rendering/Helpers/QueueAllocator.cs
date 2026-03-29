using ArctisAurora.EngineWork;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace ArctisAurora.Core.Rendering.Helpers
{
    public struct QueueCapability
    {
        public QueueFlags flag;
        public int familyIndex;   // -1 = not available
        public int defaultIndex;  // -1 = not available, 0 = shares with another
        public int count;
    }
    
    public unsafe class QueueAllocator
    {
        public QueueFamilyProperties[] properties; 
        private Dictionary<QueueFlags, QueueCapability> _capabilities = new Dictionary<QueueFlags, QueueCapability>();
        public int presentFamilyIndex = -1;

        public QueueAllocator(Vk vk, ref PhysicalDevice gpu)
        {
            uint count = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(gpu, &count, null);
            properties = new QueueFamilyProperties[count];
            vk.GetPhysicalDeviceQueueFamilyProperties(gpu, &count, properties);

            HashSet<QueueFlags> allFlags = new HashSet<QueueFlags>();
            for (int i = 0; i < properties.Length; i++)
            {
                QueueFlags f = properties[i].QueueFlags;
                foreach (QueueFlags bit in Enum.GetValues(typeof(QueueFlags)))
                    if (f.HasFlag(bit)) allFlags.Add(bit);
            }

            foreach (QueueFlags flag in allFlags)
            {
                QueueCapability cap = new QueueCapability { flag = flag, familyIndex = -1, defaultIndex = -1, count = 0 };
                int bestScore = int.MaxValue;
                for (int i = 0; i < properties.Length; i++)
                {
                    if (!properties[i].QueueFlags.HasFlag(flag)) continue;
                    int extraBits = BitCount((uint)(properties[i].QueueFlags & ~flag));
                    if (extraBits < bestScore)
                    {
                        bestScore = extraBits;
                        cap.familyIndex = i;
                        cap.defaultIndex = 0;
                        cap.count = (int)properties[i].QueueCount;
                    }
                }
                _capabilities[flag] = cap;
            }

            for (int i = 0; i < properties.Length; i++)
            {
                Engine.window.driverSurface.GetPhysicalDeviceSurfaceSupport(gpu, (uint)i, Engine.window.surface, out Bool32 supported);
                if (supported)
                {
                    presentFamilyIndex = i;
                    break;
                }
            }

            if (presentFamilyIndex == -1)
                throw new Exception("No queue family supports presentation to the window surface");
        }

        private QueueCapability Get(QueueFlags flag)
        {
            if (_capabilities.TryGetValue(flag, out QueueCapability cap))
                return cap;
            return new QueueCapability { flag = flag, familyIndex = -1, defaultIndex = -1, count = 0 };
        }

        public int GetFamilyIndex(QueueFlags flag) => Get(flag).familyIndex;

        public bool IsAvailable(QueueFlags flag)
        {
            QueueCapability cap = Get(flag);
            return cap.familyIndex != -1 && cap.defaultIndex != -1;
        }

        public bool CanConcurrent(QueueFlags flag) => Get(flag).count > 1;

        public Queue AllocateQueue(Vk vk, Device device, QueueFlags flag)
        {
            QueueCapability cap = Get(flag);
            if (cap.familyIndex == -1)
                throw new Exception($"No queue available for flag: {flag}");
            vk.GetDeviceQueue(device, (uint)cap.familyIndex, (uint)cap.defaultIndex, out Queue queue);
            cap.defaultIndex++;
            return queue;
        }

        public Queue AllocatePresentQueue(Vk vk, Device device)
        {
            if (presentFamilyIndex == -1)
                throw new Exception("No present queue available");
            vk.GetDeviceQueue(device, (uint)presentFamilyIndex, 0, out Queue queue);
            return queue;
        }

        private static int BitCount(uint value)
        {
            int count = 0;
            while (value != 0) { count += (int)(value & 1); value >>= 1; }
            return count;
        }
    }
}