using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using System.ComponentModel;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    public static class UIXSDGenerator
    {

        private static readonly Dictionary<Type, string> TypeToXsdTypeMap = new Dictionary<Type, string>
        {
            { typeof(string), "xs:string" },
            { typeof(int), "xs:int" },
            { typeof(float), "xs:float" },
            { typeof(double), "xs:double" },
            { typeof(bool), "xs:boolean" },
            { typeof(byte), "xs:byte" },
            { typeof(short), "xs:short" },
            { typeof(long), "xs:long" },
            { typeof(uint), "xs:unsignedInt" },
            { typeof(ushort), "xs:unsignedShort" },
            { typeof(ulong), "xs:unsignedLong" },
            { typeof(char), "xs:string" },
            { typeof(decimal), "xs:decimal" },
        };

        public static void GenerateTestXSD()
        {
            try
            {
                XmlSchema schema = new XmlSchema
                {
                    TargetNamespace = "http://schemas/arctis-aurora/ui",
                    ElementFormDefault = XmlSchemaForm.Qualified
                };

                // Add namespaces
                schema.Namespaces.Add("xs", "http://www.w3.org/2001/XMLSchema");
                schema.Namespaces.Add("", "http://schemas/arctis-aurora/ui"); // default namespace

                // create abstract base element to for derived controls (non containers)
                XmlSchemaComplexType abstractControl = new XmlSchemaComplexType()
                {
                    Name = "Control",
                    IsAbstract = true
                };

                XmlSchemaChoice abstractControlChoice = new XmlSchemaChoice
                {
                    MinOccurs = 0,
                    MaxOccurs = 1,
                };

                var asm = typeof(VulkanControl).Assembly;
                var controls = asm.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_VulkanControlAttribute)t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).First()
                    }).ToList();

                foreach (var control in controls)
                {
                    XmlSchemaElement derivedElement = new XmlSchemaElement
                    {
                        Name = control.Attribute.Name,
                        SchemaTypeName = new XmlQualifiedName(control.Attribute.Name, schema.TargetNamespace)
                    };
                    abstractControlChoice.Items.Add(derivedElement);
                }

                foreach (var control in controls)
                {
                    var extensionControl = new XmlSchemaComplexContentExtension
                    {
                        BaseTypeName = new XmlQualifiedName("Control", schema.TargetNamespace)
                    };

                    XmlSchemaComplexType derivedType = new XmlSchemaComplexType
                    {
                        Name = control.Attribute.Name
                    };
                    derivedType.ContentModel = new XmlSchemaComplexContent
                    {
                        Content = extensionControl
                    };

                    var attributes = control.Type.GetProperties()
                        .Where(p => p.CanRead && p.CanWrite && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum))
                        .Select(p => new
                        {
                            Property = p,
                            XmlAttribute = (XmlAttributeAttribute?)p.GetCustomAttributes(typeof(XmlAttributeAttribute), false).FirstOrDefault()
                        }).ToList();

                    foreach (var attr in attributes)
                    {
                        XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                        {
                            Name = attr.XmlAttribute?.AttributeName ?? attr.Property.Name,
                            SchemaTypeName = new XmlQualifiedName(TypeToXsdTypeMap[attr.Property.PropertyType])
                        };
                        extensionControl.Attributes.Add(schemaAttribute);
                    }

                    schema.Items.Add(derivedType);
                }

                abstractControl.Particle = abstractControlChoice;
                schema.Items.Add(abstractControl);

                // here we do the same for containers
                XmlSchemaComplexType abstractContainer = new XmlSchemaComplexType()
                {
                    Name = "Container",
                    IsAbstract = true
                };

                XmlSchemaChoice abstractContainerChoice = new XmlSchemaChoice
                {
                    MinOccurs = 0,
                    MaxOccursString = "unbounded"
                };

                var containers = asm.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_VulkanContainerAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_VulkanContainerAttribute)t.GetCustomAttributes(typeof(A_VulkanContainerAttribute), false).First()
                    }).ToList();

                foreach (var container in containers)
                {
                    XmlSchemaElement derivedElement = new XmlSchemaElement
                    {
                        Name = container.Attribute.Name,
                        SchemaTypeName = new XmlQualifiedName(container.Attribute.Name, schema.TargetNamespace)
                    };
                    abstractContainerChoice.Items.Add(derivedElement);
                }
                abstractContainer.Particle = abstractContainerChoice;
                schema.Items.Add(abstractContainer);

                var extensionContainer = new XmlSchemaComplexContentExtension
                {
                    BaseTypeName = new XmlQualifiedName("Container", schema.TargetNamespace)
                };


                foreach (var container in containers)
                {
                    XmlSchemaComplexType derivedType = new XmlSchemaComplexType
                    {
                        Name = container.Attribute.Name
                    };
                    derivedType.ContentModel = new XmlSchemaComplexContent
                    {
                        Content = extensionContainer
                    };

                    var attributes = container.Type.GetProperties()
                        .Where(p => p.CanRead && p.CanWrite && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum))
                        .Select(p => new
                        {
                            Property = p,
                            XmlAttribute = (XmlAttributeAttribute?)p.GetCustomAttributes(typeof(XmlAttributeAttribute), false).FirstOrDefault()
                        }).ToList();

                    foreach (var attr in attributes)
                    {
                        XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                        {
                            Name = attr.XmlAttribute?.AttributeName ?? attr.Property.Name,
                            SchemaTypeName = new XmlQualifiedName(attr.XmlAttribute?.AttributeName ?? attr.Property.Name, attr.Property.GetType().Name)
                        };
                        derivedType.Attributes.Add(schemaAttribute);
                    }

                    schema.Items.Add(derivedType);
                }



                // Settings for pretty printing
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = System.Text.Encoding.UTF8
                };

                // Write schema to file
                using (var writer = XmlWriter.Create("C:\\Projects-Repositories\\Aurora\\Project-Aurora\\ParticleSimulator\\Data\\XML\\Test.xsd", settings))
                {
                    schema.Write(writer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating XSD: {ex.Message}");
                throw;
            }
        }

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
