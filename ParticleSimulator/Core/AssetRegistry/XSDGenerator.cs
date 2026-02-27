using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Serialization;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace ArctisAurora.Core.AssetRegistry
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class A_XSDElementAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;

        public string Schema { get; set; }

        public A_XSDElementAttribute(string name, string schema, string? description = "")
        {
            Name = name;
            Description = description;
            Schema = schema;
        }
    }

    public sealed class A_XSDElementPropertyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = "AllActions";

        public A_XSDElementPropertyAttribute(string name, string? category="AllActions", string? description = "")
        {
            Name = name;
            Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Method)]
    public sealed class A_XSDEnumDependencyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        public A_XSDEnumDependencyAttribute(string name, string? category = "", string? description = "")
        {
            Name = name;
            Description = description;
            Category = category;
        }
    }

    public sealed class A_XSDActionDependencyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public A_XSDActionDependencyAttribute(string name, string? category = "", string? description = "")
        {
            Name = name;
            Description = description;
            Category = category;
        }
    }

    public static class XSDGenerator
    {
        private static readonly Dictionary<Type, string> enumMap = BuildEnumMap();
        private static readonly Dictionary<Type, String> UnlistedElementMap = BuildUnlistedElementMap();
        private static readonly Dictionary<string, Type> ControlMap = BuildControlMap();
        private static readonly Dictionary<Type, string> MemberMap = new Dictionary<Type, string>
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
            { typeof(Action), "actions" }
        };


        public static void GenerateXSD()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            GenerateDependencyXSD(generalAsm);
            GenerateElementXSD(generalAsm);
        }

        private static void GenerateElementXSD(Assembly[] generalAsm)
        {
            var Elements = generalAsm.SelectMany(asm => asm.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(A_XSDElementAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_XSDElementAttribute)t.GetCustomAttributes(typeof(A_XSDElementAttribute), false).First()
                }))
                .Where(x => x.Attribute != null).ToList();

            var categorizedElements = Elements.Where(x => !string.IsNullOrEmpty(x.Attribute.Schema))
                .GroupBy(x => x.Attribute.Schema)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var category in categorizedElements)
            {
                XmlSchema elementSchema = new XmlSchema
                {
                    TargetNamespace = $"http://arctisaurora/{category.Key}",
                    ElementFormDefault = XmlSchemaForm.Qualified
                };
                elementSchema.Namespaces.Add("xsd", "http://www.w3.org/2001/XMLSchema");
                elementSchema.Namespaces.Add(category.Key, $"http://arctisaurora/{category.Key}");
                elementSchema.Namespaces.Add("actions", "http://arctisaurora/ActionDependencies");
                elementSchema.Namespaces.Add("enums", "http://arctisaurora/EnumDependencies");

                XmlSchemaImport actionImport = new XmlSchemaImport
                {
                    Namespace = "http://arctisaurora/ActionDependencies",
                    SchemaLocation = "actionSchema.xsd"
                };
                elementSchema.Includes.Add(actionImport);

                XmlSchemaImport enumImport = new XmlSchemaImport
                {
                    Namespace = "http://arctisaurora/EnumDependencies",
                    SchemaLocation = "enumSchema.xsd"
                };
                elementSchema.Includes.Add(enumImport);

                XmlSchemaChoice controlChoice = new XmlSchemaChoice
                {
                    MinOccurs = 0,
                    MaxOccurs = 1,
                };

                foreach (var element in category.Value)
                {
                    XmlSchemaElement schemaElement = new XmlSchemaElement
                    {
                        Name = element.Attribute.Name,
                        SchemaTypeName = new XmlQualifiedName($"{element.Attribute.Schema}:{element.Attribute.Name}")
                    };
                    controlChoice.Items.Add(schemaElement);
                    elementSchema.Items.Add(schemaElement);
                }

                foreach (var element in category.Value)
                {
                    XmlSchemaComplexType elementComplexType = new XmlSchemaComplexType
                    {
                        Name = element.Attribute.Name
                    };
                    var attributes = element.Type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => (m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property) &&
                        m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).Any())
                        .Select(m => new
                        {
                            Property = m,
                            XmlAttribute = (A_XSDElementPropertyAttribute?)m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).FirstOrDefault()
                        }).ToList();
                    foreach (var attribute in attributes)
                    {
                        Type memberType = attribute.Property.MemberType == MemberTypes.Field
                            ? ((FieldInfo)attribute.Property).FieldType
                            : ((PropertyInfo)attribute.Property).PropertyType;

                        var annotation = new XmlSchemaAnnotation();
                        var documentation = new XmlSchemaDocumentation();
                        documentation.Markup = new XmlNode[]
                        {
                            new XmlDocument().CreateTextNode(attribute.XmlAttribute?.Description ?? "")
                        };
                        annotation.Items.Add(documentation);

                        // check if its a list
                        if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            // lol no list implementations yet
                        }
                        else
                        {
                            string typeName = MemberMap.ContainsKey(memberType) ? MemberMap[memberType] : enumMap[memberType];
                            if (typeName == "actions")
                            {
                                typeName += $":{attribute.XmlAttribute?.Category}";
                            }
                            XmlQualifiedName qualifiedName = new XmlQualifiedName(typeName);
                            XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                            {
                                Name = attribute.XmlAttribute?.Name ?? attribute.Property.Name,
                                SchemaTypeName = qualifiedName
                            };
                            schemaAttribute.Annotation = annotation;
                            elementComplexType.Attributes.Add(schemaAttribute);
                        }
                    }

                    elementSchema.Items.Add(elementComplexType);
                }

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = System.Text.Encoding.UTF8
                };

                // Write schema to file
                string path = Paths.ENGINEXML + $"\\{category.Key}Test.xsd";
                using (var writer = XmlWriter.Create(path, settings))
                {
                    elementSchema.Write(writer);
                }
            }
        }

        private static void GenerateDependencyXSD(Assembly[] generalAsm)
        {
            GenerateEnumXSD(generalAsm);
            GenerateActionXSD(generalAsm);
        }

        private static void GenerateEnumXSD(Assembly[] generalAsm)
        {
            var enums = generalAsm.SelectMany(a => a.GetTypes())
                .Where(t => t.IsEnum && t.GetCustomAttributes(typeof(A_XSDEnumDependencyAttribute), false).Any()).ToList();

            var categorizedEnums = enums.Where(x => !string.IsNullOrEmpty(x.GetCustomAttribute<A_XSDEnumDependencyAttribute>()?.Category))
                .GroupBy(x => x.GetCustomAttribute<A_XSDEnumDependencyAttribute>()?.Category)
                .ToDictionary(g => g.Key ?? "Uncategorized", g => g.ToList());

            XmlSchema enumSchema = new XmlSchema()
            {
                TargetNamespace = "http://arctisaurora/EnumDependencies",
                ElementFormDefault = XmlSchemaForm.Qualified
            };
            enumSchema.Namespaces.Add("enums", "http://arctisaurora/EnumDependencies");
            enumSchema.Namespaces.Add("xsd", "http://www.w3.org/2001/XMLSchema");
            foreach (var category in categorizedEnums)
            {
                XmlSchemaSimpleType enumType = new XmlSchemaSimpleType
                {
                    Name = category.Key
                };
                var enumRestriction = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = new XmlQualifiedName("xs:string")
                };
                foreach (var enumValue in category.Value)
                {
                    var attribute = enumValue.GetCustomAttribute<A_XSDEnumDependencyAttribute>();
                    if (attribute != null)
                    {
                        XmlSchemaEnumerationFacet enumElement = new XmlSchemaEnumerationFacet
                        {
                            Value = attribute.Name
                        };
                        enumRestriction.Facets.Add(enumElement);
                    }
                }
                enumType.Content = enumRestriction;
                enumSchema.Items.Add(enumType);
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = System.Text.Encoding.UTF8
            };
            string path = Paths.ENGINEXML + "\\enumSchema.xsd";
            using (var writer = XmlWriter.Create(path, settings))
            {
                enumSchema.Write(writer);
            }
        }

        private static void GenerateActionXSD(Assembly[] generalAsm)
        {
            var allMethods = generalAsm.SelectMany(a => a.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Select(m => new
                {
                    Method = m,
                    DeclaringType = t,
                    Attribute = m.GetCustomAttributes(typeof(A_XSDActionDependencyAttribute), false)
                                    .FirstOrDefault() as A_XSDActionDependencyAttribute
                })
                .Where(x => x.Attribute != null))).ToList();

            var globalMethods = allMethods.Where(x => string.IsNullOrEmpty(x.Attribute.Category)).ToList();

            var categorizedMethods = allMethods.Where(x => !string.IsNullOrEmpty(x.Attribute.Category))
                .GroupBy(x => x.Attribute.Category)
                .ToDictionary(g => g.Key, g => g.ToList());


            XmlSchema actionSchema = new XmlSchema
            {
                TargetNamespace = "http://arctisaurora/ActionDependencies",
                ElementFormDefault = XmlSchemaForm.Qualified
            };
            actionSchema.Namespaces.Add("actions", "http://arctisaurora/ActionDependencies");
            actionSchema.Namespaces.Add("xsd", "http://www.w3.org/2001/XMLSchema");

            foreach(var category in categorizedMethods)
            {
                XmlSchemaSimpleType actionType = new XmlSchemaSimpleType
                {
                    Name = category.Key
                };
                var categoryActions = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = new XmlQualifiedName("xs:string")
                };

                foreach(var method in category.Value)
                {
                    XmlSchemaEnumerationFacet methodElement = new XmlSchemaEnumerationFacet
                    {
                        Value = method.Attribute.Name
                    };
                    categoryActions.Facets.Add(methodElement);
                }
                actionType.Content = categoryActions;
                actionSchema.Items.Add(actionType);
            }

            foreach(var globalMethod in globalMethods)
            {
                XmlSchemaSimpleType actionType = new XmlSchemaSimpleType
                {
                    Name = globalMethod.Attribute.Name
                };
                var categoryActions = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = new XmlQualifiedName("xs:string")
                };
                XmlSchemaEnumerationFacet methodElement = new XmlSchemaEnumerationFacet
                {
                    Value = globalMethod.Attribute.Name
                };
                categoryActions.Facets.Add(methodElement);
                actionType.Content = categoryActions;
                actionSchema.Items.Add(actionType);
            }

            XmlSchemaSimpleType allActions = new XmlSchemaSimpleType()
            {
                Name = "AllActions"
            };
            XmlSchemaSimpleTypeUnion allActionsUnion = new XmlSchemaSimpleTypeUnion()
            {
                MemberTypes = categorizedMethods.Keys
                    .Select(k => new XmlQualifiedName(k, "http://arctisaurora/ActionDependencies"))
                    .Union(globalMethods
                        .Select(m => new XmlQualifiedName(m.Attribute.Name, "http://arctisaurora/ActionDependencies")))
                    .ToArray()
            };
            allActions.Content = allActionsUnion;
            actionSchema.Items.Add(allActions);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = System.Text.Encoding.UTF8
            };

            // Write schema to file
            string path = Paths.ENGINEXML + "\\actionSchema.xsd";
            using (var writer = XmlWriter.Create(path, settings))
            {
                actionSchema.Write(writer);
            }
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
                .Where(t => t.IsEnum && t.GetCustomAttributes(typeof(A_XSDEnumDependencyAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_XSDEnumDependencyAttribute)t.GetCustomAttributes(typeof(A_XSDEnumDependencyAttribute), false).First()
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