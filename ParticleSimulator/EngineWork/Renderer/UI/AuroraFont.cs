using ArctisAurora.EngineWork.Serialization;

namespace ArctisAurora.EngineWork.Renderer.UI
{
    [@Serializable]
    public class AuroraFont
    {
        public struct OffsetTable
        {
            public uint version;
            public ushort tableCount;
        }

        public struct TableEntry
        {
            public string name;
            public uint checksum;
            public uint offset;
            public uint length;
        }

        public OffsetTable offsetTable;
        public TableEntry[] tableEntries;
    }
}
