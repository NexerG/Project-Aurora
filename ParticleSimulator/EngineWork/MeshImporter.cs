using ArctisAurora.EngineWork.Model;
using Assimp;
using Assimp.Unmanaged;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork
{
    internal class MeshImporter
    {
        public static MeshImporter Instance;

        public MeshImporter() 
        {
            Instance = this;
        }

        internal Scene ImportFBX(string filePath)
        {
            AssimpContext importer  = new AssimpContext();
            Scene scene = importer.ImportFile(filePath, PostProcessPreset.TargetRealTimeMaximumQuality);
            if (scene != null )
            {
                return scene;
            }
            else Console.WriteLine("Failed to load FBX file");
            return null;
        }
    }
}
