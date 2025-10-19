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
            { typeof(Action), "Action" }
        };
        private static readonly Dictionary<string, Type> ControlMap = BuildControlMap();

        public static void GenerateUIXSDs()
        {
            // generates the XSD for the UI XML editor
            GenerateTestXSD();
        }

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

                // actions
                var generalAsm = Assembly.GetExecutingAssembly();
                var actions = generalAsm.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttributes(typeof(A_VulkanActionAttribute), false).Any())
                    .Select(m => new
                    {
                        Name = m.Name,
                        DeclaringType = t,
                        Attribute = (A_VulkanActionAttribute)m.GetCustomAttributes(typeof(A_VulkanActionAttribute), false).First()
                    })).ToList();

                XmlSchemaSimpleType actionType = new XmlSchemaSimpleType
                {
                    Name = "Action"
                };

                var actionRestriction = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = new XmlQualifiedName("xs:string")
                };

                foreach (var action in actions)
                {
                    actionRestriction.Facets.Add(new XmlSchemaEnumerationFacet
                    {
                        Value = action.Name
                    });
                }

                actionType.Content = actionRestriction;
                schema.Items.Add(actionType);

                // ENUMS
                var asm = typeof(VulkanControl).Assembly;
                var enumTypes = asm.GetTypes()
                .Where(t => t.IsEnum && t.GetCustomAttributes(typeof(A_VulkanEnumAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_VulkanEnumAttribute)t.GetCustomAttributes(typeof(A_VulkanEnumAttribute), false).First()
                }).ToList();

                foreach (var enumType in enumTypes)
                {
                    XmlSchemaSimpleType enumSchemaType = new XmlSchemaSimpleType
                    {
                        Name = enumType.Attribute.Name
                    };
                    XmlSchemaSimpleTypeRestriction restriction = new XmlSchemaSimpleTypeRestriction
                    {
                        BaseTypeName = new XmlQualifiedName("xs:string")
                    };
                    foreach (var name in Enum.GetNames(enumType.Type))
                    {
                        restriction.Facets.Add(new XmlSchemaEnumerationFacet { Value = name });
                    }
                    enumSchemaType.Content = restriction;
                    schema.Items.Add(enumSchemaType);
                }

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


                var controls = asm.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_VulkanControlAttribute)t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).First()
                    }).ToList();

                // setup controls with their attributes
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

                    //var testAttributes = control.Type.GetFields();
                    
                    var attributes = control.Type.GetFields()
                        .Where(p => p.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).Any())
                        .Select(p => new
                        {
                            Property = p,
                            XmlAttribute = (A_VulkanControlPropertyAttribute?)p.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).FirstOrDefault()
                        }).ToList(); ;

                    foreach (var attr in attributes)
                    {
                        XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                        {
                            Name = attr.XmlAttribute?.Name ?? attr.Property.Name,
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

        public static WindowControl ParseXML(string xaml)
        {
            string path = Paths.ENGINEXML + "\\" + xaml;

            XDocument doc = XDocument.Load(path);
            XElement root = doc.Root;
            WindowControl topControl = new WindowControl();
            float pos = 1.0f;
            topControl.transform.SetWorldPosition(new Vector3D<float>(pos, 0, 0));
            RecursiveParse(root, topControl, pos);
            return topControl;
        }

        private static void RecursiveParse(XElement root, VulkanControl topControl, float pos)
        {
            foreach (XAttribute attr in root.Attributes())
            {
                var prop = topControl.GetType().GetField(attr.Name.LocalName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    if(prop.FieldType == typeof(Action))
                    {
                        MethodInfo? methodInfo = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        .FirstOrDefault(m =>
                                m.GetCustomAttributes(typeof(A_VulkanActionAttribute), false).Any() &&
                                string.Equals(m.Name, attr.Value, StringComparison.OrdinalIgnoreCase));

                        if (methodInfo == null)
                            throw new Exception($"Action method '{attr.Value}' not found in A_VulkanControlPropertyAttribute.");

                        Action actionDelegate = (Action)Delegate.CreateDelegate(typeof(Action), methodInfo);
                        var current = (Action?)prop.GetValue(topControl);
                        current += actionDelegate;
                        prop.SetValue(topControl, current);
                        continue;
                    }
                    object? value = TypeDescriptor.GetConverter(prop.FieldType).ConvertFromInvariantString(attr.Value);
                    prop.SetValue(topControl, value);
                }
            }

            foreach (var element in root.Elements())
            {
                if (!ControlMap.TryGetValue(element.Name.LocalName, out var controlType))
                    throw new Exception($"Unknown control type: {element.Name}");
                VulkanControl c = (VulkanControl)Activator.CreateInstance(controlType);
                pos -= 0.01f;
                c.transform.SetWorldPosition(new Vector3D<float>(pos, 0, 0));
                topControl.AddChild(c);

                RecursiveParse(element, topControl.child, pos);
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
    }
}