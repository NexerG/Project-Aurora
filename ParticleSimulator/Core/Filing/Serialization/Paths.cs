using ArctisAurora.EngineWork;

namespace ArctisAurora.Core.Filing.Serialization
{
    public static class Paths
    {
        public static readonly string BUILD_UI = GetPath("Data\\XML");

        public static readonly string FONTS = GetPath("Data\\Fonts");
        public static readonly string DATA = GetPath("Data");
        public static readonly string UIMASKS = GetPath("Data\\UIMasks");
        public static readonly string XML = GetPath("Data\\XML");
        public static readonly string XMLSCHEMAS = GetPath("Data\\XML\\Schemas");
        public static readonly string XMLDOCUMENTS = GetPath("Data\\XML\\Documents");
        public static readonly string XMLDOCUMENTS_INPUTS = GetPath("Data\\XML\\Documents\\Inputs");
        public static readonly string XMLDOCUMENTS_SAMPLERS = GetPath("Data\\XML\\Documents\\Samplers");
        public static readonly string BOOTSTRAP = GetPath("Data\\XML\\Documents\\Bootstrap.XML");
        public static readonly string SCENES = GetPath("Data\\Scenes");

        private static string GetPath(string path)
        {
            bool isDebug = Engine.isDebug;

            if (isDebug)
            {
                string devRelativePath = Path.GetFullPath(Path.Combine("..", "..", "..", path));
                return devRelativePath;
            }

            string deployRelativePath = Path.Combine(AppContext.BaseDirectory, path);
            return deployRelativePath;
        }
    }
}
