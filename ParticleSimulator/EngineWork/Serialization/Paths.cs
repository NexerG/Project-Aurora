using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Serialization
{
    internal static class Paths
    {
        public static readonly string FONTS = GetPath("Data\\Fonts");
        public static readonly string DATA = GetPath("Data");
        public static readonly string UIMASKS = GetPath("Data\\UIMasks");

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
