using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using ArctisAurora.EngineWork.Serialization;
using Assimp;
using Silk.NET.Maths;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;
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
            { typeof(Action), "Action" }
        };
        private static readonly Dictionary<Type, string> enumMap = BuildEnumMap();
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
                // assembly for reflection
                var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
                
                
                XmlSchema schema = new XmlSchema
                {
                    TargetNamespace = "http://schemas/arctis-aurora/ui",
                    ElementFormDefault = XmlSchemaForm.Qualified
                };

                // Add namespaces
                schema.Namespaces.Add("xs", "http://www.w3.org/2001/XMLSchema");
                schema.Namespaces.Add("", "http://schemas/arctis-aurora/ui"); // default namespace

                // actions
                var actions = generalAsm.SelectMany(a => a.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttributes(typeof(A_VulkanActionAttribute), false).Any())
                    .Select(m => new
                    {
                        Name = m.Name,
                        DeclaringType = t,
                        Attribute = (A_VulkanActionAttribute)m.GetCustomAttributes(typeof(A_VulkanActionAttribute), false).First()
                    }))).ToList();

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
                var enumTypes = generalAsm.SelectMany(a => a.GetTypes()
                .Where(t => t.IsEnum && t.GetCustomAttributes(typeof(A_VulkanEnumAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_VulkanEnumAttribute)t.GetCustomAttributes(typeof(A_VulkanEnumAttribute), false).First()
                })).ToList();

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
                XmlSchemaChoice abstractContainerChoice = new XmlSchemaChoice
                {
                    MinOccurs = 0,
                    MaxOccursString = "unbounded"
                };


                var controls = generalAsm.SelectMany(a => a.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_VulkanControlAttribute)t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).First()
                    })).ToList();

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

                    var testattributes = control.Type.GetFields();

                    //var attributes = control.Type.GetFields()
                    //    .Where(p => p.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).Any())
                    //    .Select(p => new
                    //    {
                    //        Property = p,
                    //        XmlAttribute = (A_VulkanControlPropertyAttribute?)p.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).FirstOrDefault()
                    //    }).ToList();

                    var attributes = control.Type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => (m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property) &&
                            m.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).Any())
                        .Select(m => new
                        {
                            Property = m,
                            XmlAttribute = (A_VulkanControlPropertyAttribute?)m.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).FirstOrDefault()
                        }).ToList();

                    foreach (var attr in attributes)
                    {
                        Type memberType = attr.Property.MemberType == MemberTypes.Field
                            ? ((FieldInfo)attr.Property).FieldType
                            : ((PropertyInfo)attr.Property).PropertyType;
                        string name = TypeToXsdTypeMap.ContainsKey(memberType) ? TypeToXsdTypeMap[memberType] : enumMap[memberType];
                        XmlQualifiedName typeName = new XmlQualifiedName(name);
                        XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                        {
                            Name = attr.XmlAttribute?.Name ?? attr.Property.Name,
                            SchemaTypeName = typeName
                        };
                        var annotation = new XmlSchemaAnnotation();
                        var documentation = new XmlSchemaDocumentation();
                        documentation.Markup = new XmlNode[]
                        {
                            new XmlDocument().CreateTextNode(attr.XmlAttribute?.Description ?? "")
                        };
                        annotation.Items.Add(documentation);
                        schemaAttribute.Annotation = annotation;

                        extensionControl.Attributes.Add(schemaAttribute);
                    }

                    schema.Items.Add(derivedType);
                    schema.Items.Add(xmlSchemaElement);
                    abstractControlChoice.Items.Add(xmlSchemaElement);
                    abstractContainerChoice.Items.Add(xmlSchemaElement);
                }
                abstractControl.Particle = abstractControlChoice;
                schema.Items.Add(abstractControl);

                // here we do the same for containers
                XmlSchemaComplexType abstractContainer = new XmlSchemaComplexType()
                {
                    Name = "Container",
                    IsAbstract = true
                };

                var containers = generalAsm.SelectMany(a => a.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_VulkanContainerAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_VulkanContainerAttribute)t.GetCustomAttributes(typeof(A_VulkanContainerAttribute), false).First()
                    })).ToList();

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
                    abstractControlChoice.Items.Add(derivedElement);
                    abstractContainerChoice.Items.Add(derivedElement);
                    
                    XmlSchemaComplexType derivedType = new XmlSchemaComplexType
                    {
                        Name = container.Attribute.Name
                    };
                    derivedType.ContentModel = new XmlSchemaComplexContent
                    {
                        Content = extensionContainer
                    };

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
                string path = Paths.ENGINEXML + "\\UI.xsd";
                using (var writer = XmlWriter.Create(path, settings))
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
            ResolveAttributes(root, topControl);
            EntityManager.uiTree = topControl;
            Vector3D<float> pos = new Vector3D<float>(topControl.width / 2, topControl.height / 2, -10.0f);
            topControl.transform.SetWorldPosition(pos);
            topControl.transform.SetWorldScale(new Vector3D<float>(topControl.width, topControl.height, 1.0f));
            RecursiveParse(root, topControl);
            return topControl;
        }

        private static void RecursiveParse(XElement root, VulkanControl topControl)
        {
            foreach (var element in root.Elements())
            {
                if (!ControlMap.TryGetValue(element.Name.LocalName, out var controlType))
                    throw new Exception($"Unknown control type: {element.Name}");
                VulkanControl c = (VulkanControl)Activator.CreateInstance(controlType);
                ResolveAttributes(element, c);
                topControl.AddChild(c);

                RecursiveParse(element, c);
            }
        }

        private static void ResolveAttributes(XElement root, VulkanControl topControl)
        {
            foreach (XAttribute attr in root.Attributes())
            {
                var prop = topControl.GetType().GetMember(attr.Name.LocalName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase).FirstOrDefault();
                if (prop != null)
                {
                    Type memberType = prop.MemberType == MemberTypes.Field ? ((FieldInfo)prop).FieldType : ((PropertyInfo)prop).PropertyType;
                    if (memberType == typeof(Action))
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
                        if (prop is PropertyInfo propertyInfo)
                        {
                            Action current = (Action?)propertyInfo.GetValue(topControl);
                            current += actionDelegate;
                            propertyInfo.SetValue(topControl, current);
                            continue;
                        }
                        if(prop is FieldInfo fieldInfo)
                        {
                            Action current = (Action?)fieldInfo.GetValue(topControl);
                            current += actionDelegate;
                            fieldInfo.SetValue(topControl, current);
                            continue;
                        }
                    }
                    else if (memberType.IsEnum)
                    {
                        if (prop is PropertyInfo propertyInfo)
                        {
                            object enumValue = Enum.Parse(propertyInfo.PropertyType, attr.Value);
                            propertyInfo.SetValue(topControl, enumValue);
                            continue;
                        }
                        if (prop is FieldInfo fieldInfo)
                        {
                            object enumValue = Enum.Parse(fieldInfo.FieldType, attr.Value);
                            fieldInfo.SetValue(topControl, enumValue);
                            continue;
                        }
                        continue;
                    }
                    else
                    {
                        if (prop is PropertyInfo propertyInfo)
                        {
                            object value = TypeDescriptor.GetConverter(propertyInfo.PropertyType).ConvertFromInvariantString(attr.Value);
                            propertyInfo.SetValue(topControl, value);
                            continue;
                        }
                        if (prop is FieldInfo fieldInfo)
                        {
                            object value = TypeDescriptor.GetConverter(fieldInfo.FieldType).ConvertFromInvariantString(attr.Value);
                            fieldInfo.SetValue(topControl, value);
                            continue;
                        }
                    }
                }
                topControl.UpdateControlData();
            }
        }

        private static Dictionary<string, Type> BuildControlMap()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();

            return generalAsm.SelectMany(asm => asm.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(VulkanControl).IsAssignableFrom(t) && t.GetCustomAttribute<A_VulkanContainerAttribute>() != null || t.GetCustomAttribute<A_VulkanControlAttribute>() != null)
                    .Select(t => new
                    {
                        Type = t,
                        Tag = t.GetCustomAttribute<A_VulkanControlAttribute>()?.Name ?? t.GetCustomAttribute<A_VulkanContainerAttribute>()?.Name ?? t.Name
                    })).ToDictionary(x => x.Tag, x => x.Type);
        }

        private static Dictionary<Type, string> BuildEnumMap()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            var enumTypes = generalAsm.SelectMany(asm => asm.GetTypes()
                .Where(t => t.IsEnum && t.GetCustomAttributes(typeof(A_VulkanEnumAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_VulkanEnumAttribute)t.GetCustomAttributes(typeof(A_VulkanEnumAttribute), false).First()
                })).ToList();
            Dictionary<Type, string> map = new Dictionary<Type, string>();
            foreach (var enumType in enumTypes)
            {
                map[enumType.Type] = enumType.Attribute.Name;
            }
            return map;
        }
    }
}