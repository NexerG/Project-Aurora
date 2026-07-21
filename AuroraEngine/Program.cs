using ArctisAurora.EngineWork.Rendering.UI;
using System.Runtime.CompilerServices;

namespace ArctisAurora
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            /*if(args.Length > 0 && args[0] == "--xsd-generate")
            {
                Console.WriteLine("Generating XSD for VulkanControl...");
                UIXSDGenerator.GenerateVulkanControlXsd();
                return; // Exit after generating XSD
            }
            else
            {*/
                // To customize application configuration such as set high DPI settings or default font,
                // see https://aka.ms/applicationconfiguration.
                ApplicationConfiguration.Initialize();
                Application.Run(new Frame());
            //}
        }
    }
}