using ArctisAurora.EngineWork.Rendering.UI.Controls;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    public static class UIXSDGenerator
    {
        public static void GenerateVulkanControlXsd()
        {
            try
            {
                var schemas = new XmlSchemas();
                var exporter = new XmlSchemaExporter(schemas);
                var importer = new XmlReflectionImporter();

                var vulkanControlType = typeof(VulkanControl);
                var assembly = vulkanControlType.Assembly;

                var derivedTypes = assembly.GetTypes()
                            .Where(t => t != vulkanControlType &&
                                        vulkanControlType.IsAssignableFrom(t) &&
                                        !t.IsAbstract && t.IsPublic).ToList();

                Console.WriteLine($"Found {derivedTypes.Count} subclasses of VulkanControl");
                foreach (var derivedType in derivedTypes)
                {
                    Console.WriteLine($"Generating XSD for: {derivedType.Name}");
                    var mapping = importer.ImportTypeMapping(derivedType);
                    exporter.ExportTypeMapping(mapping);
                }

                var outputPath = @"C:\Projects-Repositories\Aurora\Project-Aurora\ParticleSimulator\Data\XML\EngineUI.xsd";
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using (var writer = new StreamWriter(outputPath))
                {
                    foreach (XmlSchema schema in schemas)
                    {
                        schema.Write(writer);
                    }
                }

                Console.WriteLine($"XSD generated successfully for {derivedTypes.Count} types!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating XSD: {ex.Message}");
                throw;
            }
        }
    }
}
