using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
using Silk.NET.Maths;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    public static class VulkanUIHandler
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
            { typeof(Enum), "xs:string" },
            { typeof(Vector3D<float>), "vec3" }
        };
        private static readonly Dictionary<string, Type> ControlMap = BuildControlMap();

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

                XmlSchemaSimpleType vec3 = new XmlSchemaSimpleType
                {
                    Name = "vec3",
                    Content = new XmlSchemaSimpleTypeRestriction
                    {
                        BaseTypeName = new XmlQualifiedName("xs:string"),
                        Facets =
                        {
                            new XmlSchemaPatternFacet
                            {
                                Value = @"-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?"
                            }
                        }
                    }
                };
                schema.Items.Add(vec3);

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
                    var extensionControl = new XmlSchemaComplexContentExtension
                    {
                        BaseTypeName = new XmlQualifiedName("Control", schema.TargetNamespace)
                    };

                    XmlSchemaElement xmlSchemaElement = new XmlSchemaElement
                    {
                        Name = control.Attribute.Name,
                        SchemaTypeName = new XmlQualifiedName(control.Attribute.Name, schema.TargetNamespace)
                    };

                    XmlSchemaComplexType derivedType = new XmlSchemaComplexType
                    {
                        Name = control.Attribute.Name
                    };

                    derivedType.ContentModel = new XmlSchemaComplexContent
                    {
                        Content = extensionControl
                    };

                    //var attributes = control.Type.GetProperties()
                    //    .Where(p => p.CanRead && p.CanWrite && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum))
                    //    .Select(p => new
                    //    {
                    //        Property = p,
                    //        XmlAttribute = (XmlAttributeAttribute?)p.GetCustomAttributes(typeof(XmlAttributeAttribute), false).FirstOrDefault()
                    //    }).ToList();

                    Console.WriteLine(control.Type);
                    Console.WriteLine(control.Type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                    var attributes = control.Type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.GetCustomAttributes(typeof(XmlAttributeAttribute), false).Any())
                        .Select(p => new
                        {
                            Property = p,
                            XmlAttribute = (XmlAttributeAttribute?)p.GetCustomAttributes(typeof(XmlAttributeAttribute), false).FirstOrDefault()
                        }).ToList(); ;

                    foreach (var attr in attributes)
                    {
                        XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                        {
                            Name = attr.XmlAttribute?.AttributeName ?? attr.Property.Name,
                            SchemaTypeName = new XmlQualifiedName(TypeToXsdTypeMap[attr.Property.FieldType])
                        };
                        extensionControl.Attributes.Add(schemaAttribute);
                    }

                    schema.Items.Add(derivedType);
                    schema.Items.Add(xmlSchemaElement);
                    abstractControlChoice.Items.Add(xmlSchemaElement);
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

                var extensionContainer = new XmlSchemaComplexContentExtension
                {
                    BaseTypeName = new XmlQualifiedName("Container", schema.TargetNamespace)
                };


                foreach (var container in containers)
                {
                    XmlSchemaElement derivedElement = new XmlSchemaElement
                    {
                        Name = container.Attribute.Name,
                        SchemaTypeName = new XmlQualifiedName(container.Attribute.Name, schema.TargetNamespace)
                    };
                    abstractContainerChoice.Items.Add(derivedElement);
                    
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
                    schema.Items.Add(derivedElement);
                }
                abstractContainer.Particle = abstractContainerChoice;
                schema.Items.Add(abstractContainer);

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

        public static void ParseXML(string xaml)
        {
            string path = Paths.UIXAML + "\\" + xaml;

            XDocument doc = XDocument.Load(path);
            XElement root = doc.Root;
            WindowControl topControl = new WindowControl();
            RecursiveParse(root, topControl);
            //VulkanControl control = CreateControlFromXML(root);
            // Now 'control' is the root control created from the XML
        }

        private static void RecursiveParse(XElement root, VulkanControl topControl)
        {
            foreach (XAttribute attr in root.Attributes())
            {
                var prop = topControl.GetType().GetProperty(attr.Name.LocalName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite)
                {
                    object? value = TypeDescriptor.GetConverter(prop.PropertyType).ConvertFromInvariantString(attr.Value);
                    prop.SetValue(topControl, value);
                }
            }

            foreach (var element in root.Elements())
            {
                if (!ControlMap.TryGetValue(element.Name.LocalName, out var controlType))
                    throw new Exception($"Unknown control type: {element.Name}");
                topControl.AddChild((VulkanControl)Activator.CreateInstance(controlType));

                RecursiveParse(element, topControl.child);
            }
        }

        private static Dictionary<string, Type> BuildControlMap()
        {
            var asm = typeof(VulkanControl).Assembly;
            return asm.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(VulkanControl).IsAssignableFrom(t) && t.GetCustomAttribute<A_VulkanControlAttribute>() != null)
                    .Select(t => new
                    {
                        Type = t,
                        Tag = t.GetCustomAttribute<A_VulkanControlAttribute>()?.Name ?? t.Name
                    })
                    .ToDictionary(x => x.Tag, x => x.Type);
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