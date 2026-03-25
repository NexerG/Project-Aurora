using ArctisAurora.Core.AssetRegistry;
using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
using Microsoft.Extensions.DependencyModel;
using System.Reflection;
using System.Security.Cryptography.Pkcs;
using System.Xml.Linq;
using static ArctisAurora.Core.UISystem.Controls.VulkanControl;

namespace ArctisAurora.EngineWork
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple =true)]
    public sealed class A_BootstrapStageAttribute : Attribute
    {
        public BootstrapStage stage { get; set; }

        public A_BootstrapStageAttribute(BootstrapStage stage)
        {
            this.stage = stage;
        }
    }

    public interface IBootstrap
    {
        public static abstract void Bootstrap(BootstrapStage? stage);
    }

    public enum BootstrapStage
    {
        PreGPUAPI,
        PostGPUAPI,
        PrePhysicsNOTMPLEMENTED,
        PostPhysicsNOTMPLEMENTED,
    }

    internal static class Bootstrapper
    {
        public static void Bootstrap(BootstrapStage stage)
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            var stagedMethods = generalAsm.SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.IsDefined(typeof(A_BootstrapStageAttribute), false) &&
                            m.GetCustomAttributes<A_BootstrapStageAttribute>().Any(a => a.stage == stage)).ToList();

            foreach (var method in stagedMethods)
                method.Invoke(null, new object[] { stage });
        }
    }
}