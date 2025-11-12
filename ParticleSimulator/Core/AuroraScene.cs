using ArctisAurora.EngineWork.EngineEntity;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork
{
    public class AuroraScene
    {
        [@Serializable]
        public string sceneName = "NewScene";

        [@Serializable]
        public List<Entity> entities = new List<Entity>();

        public static void SaveScene(AuroraScene scene)
        {
            Serializer.SerializeAttributed<AuroraScene>(scene, Paths.SCENES + $"\\{scene.sceneName}.as");
        }

        public static void deserializeScene(string scenename)
        {
            //AuroraScene scene = Serializer.DeserializeAttributed<AuroraScene>(scenePath);
            //foreach (Entity e in scene.entities)
            //{
            //    EntityManager.AddEntity(e);
            //}
        }
    }
}
