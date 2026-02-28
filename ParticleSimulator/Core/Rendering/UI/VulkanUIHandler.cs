using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Rendering.UI.Controls.Containers;
using ArctisAurora.EngineWork.Serialization;
using Silk.NET.Maths;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace ArctisAurora.EngineWork.Rendering.UI
{
    public static class VulkanUIHandler
    {
        private static readonly Dictionary<Type, string> AttributeMap = new Dictionary<Type, string>
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
        private static readonly Dictionary<Type, String> UnlistedElementMap = BuildUnlistedElementMap();
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

                #region actions
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
                #endregion

                #region enums
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
                #endregion

                #region unlisted elements
                // unlisted elements
                var unlistedTypes = generalAsm.SelectMany(a => a.GetTypes()
                .Where(t => t.IsClass && t.GetCustomAttributes(typeof(A_VulkanControlElementAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_VulkanControlElementAttribute)t.GetCustomAttributes(typeof(A_VulkanControlElementAttribute), false).First()
                })).ToList();

                foreach (var unlistedType in unlistedTypes)
                {
                    //XmlSchemaElement unlistedElement = new XmlSchemaElement
                    //{
                    //    Name = unlistedType.Attribute.Name
                    //};

                    XmlSchemaComplexType complexType = new XmlSchemaComplexType
                    {
                        Name = unlistedType.Attribute.Name
                    };

                    //unlistedElement.SchemaType = complexType;

                    var attributes = unlistedType.Type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
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
                        string name = AttributeMap.ContainsKey(memberType) ? AttributeMap[memberType] : enumMap[memberType];
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
                        complexType.Attributes.Add(schemaAttribute);
                    }

                    schema.Items.Add(complexType);
                }
                #endregion

                #region controls
                // controls
                var controls = generalAsm.SelectMany(a => a.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_VulkanControlAttribute)t.GetCustomAttributes(typeof(A_VulkanControlAttribute), false).First()
                    })).ToList();

                // choice for control complex types
                XmlSchemaChoice controlChoice = new XmlSchemaChoice
                {
                    MinOccurs = 0,
                    MaxOccurs =1
                };
                // choice for container complex types
                XmlSchemaChoice containerChoice = new XmlSchemaChoice
                {
                    MinOccurs = 0,
                    MaxOccursString ="unbounded"
                };

                // elements
                foreach (var control in controls)
                {
                    XmlSchemaElement Control = new XmlSchemaElement()
                    {
                        Name = control.Attribute.Name,
                        SchemaTypeName = new XmlQualifiedName(control.Attribute.Name, schema.TargetNamespace)
                    };
                    controlChoice.Items.Add(Control);
                    containerChoice.Items.Add(Control);
                    schema.Items.Add(Control);
                }

                // complex types
                foreach (var control in controls)
                {
                    XmlSchemaComplexType complexType = new XmlSchemaComplexType
                    {
                        Name = control.Attribute.Name
                    };
                    // check for attributes
                    var attributes = control.Type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => (m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property) &&
                        m.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).Any())
                        .Select(m => new
                        {
                            Property = m,
                            XmlAttribute = (A_VulkanControlPropertyAttribute?)m.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).FirstOrDefault()
                        }).ToList();

                    if (control.Type.IsSubclassOf(typeof(AbstractContainerControl)))
                    {
                        complexType.Particle = CopyChoice(containerChoice);
                    }
                    else
                    {
                        complexType.Particle = CopyChoice(controlChoice);
                    }

                    foreach (var attr in attributes)
                    {
                        Type memberType = attr.Property.MemberType == MemberTypes.Field
                            ? ((FieldInfo)attr.Property).FieldType
                            : ((PropertyInfo)attr.Property).PropertyType;

                        var annotation = new XmlSchemaAnnotation();
                        var documentation = new XmlSchemaDocumentation();
                        documentation.Markup = new XmlNode[]
                        {
                            new XmlDocument().CreateTextNode(attr.XmlAttribute?.Description ?? "")
                        };
                        annotation.Items.Add(documentation);

                        // check if its a list
                        if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            memberType = memberType.GetGenericArguments()[0];
                            string typeName = control.Attribute.Name + "." + UnlistedElementMap[memberType];
                            XmlQualifiedName xmlQualifiedName = new XmlQualifiedName(typeName);

                            XmlSchemaComplexType listComplexType = new XmlSchemaComplexType()
                            {
                                Name = control.Attribute.Name + "." + (attr.XmlAttribute?.Name ?? attr.Property.Name)
                            };
                            listComplexType.Particle = new XmlSchemaSequence();
                            XmlSchemaElement listElement = new XmlSchemaElement
                            {
                                Name = UnlistedElementMap[memberType],
                                SchemaTypeName = new XmlQualifiedName(UnlistedElementMap[memberType]),
                                MinOccurs = 0,
                                MaxOccursString = "unbounded",
                            };
                            schema.Items.Add(listComplexType);
                            ((XmlSchemaSequence)listComplexType.Particle).Items.Add(listElement);

                            XmlSchemaElement schemaElement = new XmlSchemaElement
                            {
                                Name = control.Attribute.Name + "." + (attr.XmlAttribute?.Name ?? attr.Property.Name),
                                SchemaTypeName = xmlQualifiedName,
                                MinOccurs = 0,
                                MaxOccurs = 1
                            };
                            schemaElement.Annotation = annotation;
                            ((XmlSchemaChoice)complexType.Particle).Items.Add(schemaElement);
                        }
                        else
                        {
                            string typeName = AttributeMap.ContainsKey(memberType) ? AttributeMap[memberType] : enumMap[memberType];
                            XmlQualifiedName qualifiedName = new XmlQualifiedName(typeName);
                            XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                            {
                                Name = attr.XmlAttribute?.Name ?? attr.Property.Name,
                                SchemaTypeName = qualifiedName
                            };
                            schemaAttribute.Annotation = annotation;
                            complexType.Attributes.Add(schemaAttribute);
                        }
                    }
                    schema.Items.Add(complexType);
                }
                #endregion

                // Settings for pretty printing
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = System.Text.Encoding.UTF8
                };

                // Write schema to file
                string path = Paths.XMLSCHEMAS + "\\UI.xsd";
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
            string path = Paths.XMLDOCUMENTS + "\\" + xaml;

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
                {
                    string name = element.Name.LocalName;
                    int idx = name.LastIndexOf('.');
                    string listName = name.Substring(idx + 1);
                    if (UnlistedElementMap.Values.Contains(listName))
                    {
                        var unlistedType = UnlistedElementMap.FirstOrDefault(x => x.Value == listName).Key;
                        Type listType = typeof(List<>).MakeGenericType(unlistedType);
                        var listInstance = Activator.CreateInstance(listType);
                        ResolveObjects(element, listInstance, unlistedType);
                        var member = (MemberInfo)topControl.GetType()
                                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .FirstOrDefault(p => p.PropertyType == listType)
                            ?? (MemberInfo)topControl.GetType()
                                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .FirstOrDefault(f => f.FieldType == listType);
                        if (member is PropertyInfo prop)
                            prop.SetValue(topControl, listInstance);
                        else if (member is FieldInfo field)
                            field.SetValue(topControl, listInstance);
                    }
                }
                else
                {
                    VulkanControl c = (VulkanControl)Activator.CreateInstance(controlType);
                    ResolveAttributes(element, c);
                    topControl.AddChild(c);
                    RecursiveParse(element, c);
                }
            }
        }

        private static void ResolveAttributes(XElement root, object topControl)
        {
            foreach (XAttribute attr in root.Attributes())
            {
                var prop = topControl.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase).FirstOrDefault(m =>
                {
                    var a = m.GetCustomAttributes(typeof(A_VulkanControlPropertyAttribute), true).FirstOrDefault() as A_VulkanControlPropertyAttribute;
                    if (a != null)
                    {
                        return string.Equals(a.Name, attr.Name.LocalName, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                });

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
                if (topControl.GetType() == typeof(VulkanControl))
                {
                    ((VulkanControl)topControl).UpdateControlData();

                }
            }
        }

        private static void ResolveObjects(XElement root, object list, Type childType)
        {
            foreach (XElement element in root.Elements())
            {
                object? instance = Activator.CreateInstance(childType);
                ResolveAttributes(element, instance);
                if (instance != null)
                {
                    ((IList)list).Add(instance);
                }
            }
        }

        private static XmlSchemaChoice CopyChoice(XmlSchemaChoice original)
        {
            XmlSchemaChoice copy = new XmlSchemaChoice
            {
                MinOccurs = original.MinOccurs,
                MaxOccursString = original.MaxOccursString
            };
            foreach (XmlSchemaObject item in original.Items)
            {
                copy.Items.Add(item);
            }
            return copy;
        }

        #region MapBuilders
        private static Dictionary<string, Type> BuildControlMap()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();

            return generalAsm.SelectMany(asm => asm.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(VulkanControl).IsAssignableFrom(t) && t.GetCustomAttribute<A_VulkanControlAttribute>() != null)
                    .Select(t => new
                    {
                        Type = t,
                        Tag = t.GetCustomAttribute<A_VulkanControlAttribute>()?.Name ?? t.Name
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

        private static Dictionary<Type, String> BuildUnlistedElementMap()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            return generalAsm.SelectMany(asm => asm.GetTypes()
                    .Where(t => t.IsClass && t.GetCustomAttributes(typeof(A_VulkanControlElementAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Tag = t.GetCustomAttribute<A_VulkanControlElementAttribute>()?.Name ?? t.Name
                    })).ToDictionary(x => x.Type, x => x.Tag);
        }
        #endregion
    }
}