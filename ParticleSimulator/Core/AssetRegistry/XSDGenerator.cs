using ArctisAurora.EngineWork.Rendering.UI.Controls;
using ArctisAurora.EngineWork.Serialization;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace ArctisAurora.Core.AssetRegistry
{
    #region Attributes
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class A_XSDElementAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Schema { get; set; }
        public string TypeDepedency { get; set; } = "Uncategorized";

        public A_XSDElementAttribute(string name, string schema, string? typeDependency = "Uncategorized", string? description = "")
        {
            Name = name;
            Description = description;
            Schema = schema;
            TypeDepedency = typeDependency;
        }
    }

    public sealed class A_XSDElementPropertyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";

        public A_XSDElementPropertyAttribute(string name, string? category= "Uncategorized", string? description = "")
        {
            Name = name;
            Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class)]
    public sealed class A_XSDTypeAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public string PatternValue { get; set; } = string.Empty;

        public A_XSDTypeAttribute(string name, string? category = "Uncategorized", string? patternValue="", string? description = "")
        {
            Name = name;
            Description = description;
            Category = category;
            PatternValue = patternValue;
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
    #endregion

    public interface IXMLParser
    {
        void ParseXML(string xmlName);
    }
    public static class XSDGenerator
    {
        private static readonly Dictionary<Type, string> typeMap = BuildTypeMap();
        private static readonly Dictionary<string, Type> XMLElementMap;
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
                elementSchema.Namespaces.Add("xs", "http://www.w3.org/2001/XMLSchema");
                elementSchema.Namespaces.Add(category.Key, $"http://arctisaurora/{category.Key}");
                
                elementSchema.Namespaces.Add("actions", "http://arctisaurora/ActionDependencies");
                XmlSchemaImport actionImport = new XmlSchemaImport
                {
                    Namespace = "http://arctisaurora/ActionDependencies",
                    SchemaLocation = "actionSchema.xsd"
                };
                elementSchema.Includes.Add(actionImport);

                elementSchema.Namespaces.Add("types", $"http://arctisaurora/Aurora{category.Key}Types");
                XmlSchemaImport typeImport = new XmlSchemaImport
                {
                    Namespace = $"http://arctisaurora/Aurora{category.Key}Types",
                    SchemaLocation = $"{category.Key}TypeSchema.xsd"
                };
                elementSchema.Includes.Add(typeImport);

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
                            memberType = memberType.GetGenericArguments()[0];
                            string typeName = MemberMap.ContainsKey(memberType) ? MemberMap[memberType] : typeMap[memberType];
                            if (typeName == "actions")
                            {
                                typeName += $":{attribute.XmlAttribute?.Category}";
                            }
                            if (!MemberMap.ContainsKey(memberType))
                            {
                                typeName = $"types:{typeName}";
                            }
                            XmlQualifiedName qualifiedName = new XmlQualifiedName(typeName);
                            XmlSchemaElement listElement = new XmlSchemaElement
                            {
                                Name = attribute.XmlAttribute?.Name ?? attribute.Property.Name,
                                SchemaTypeName = qualifiedName,
                                MinOccurs = 0,
                                MaxOccursString = "unbounded"
                            };
                            listElement.Annotation = annotation;
                            elementComplexType.Particle = new XmlSchemaSequence
                            {
                                Items = { listElement }
                            };
                        }
                        else
                        {
                            string typeName = MemberMap.ContainsKey(memberType) ? MemberMap[memberType] : typeMap[memberType];
                            if (typeName == "actions")
                            {
                                typeName += $":{attribute.XmlAttribute?.Category}";
                            }
                            if (!MemberMap.ContainsKey(memberType))
                            {
                                typeName = $"types:{typeName}";
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
                string path = Paths.XMLSCHEMAS + $"\\{category.Key}Test.xsd";
                using (var writer = XmlWriter.Create(path, settings))
                {
                    elementSchema.Write(writer);
                }
            }
        }

        private static void GenerateDependencyXSD(Assembly[] generalAsm)
        {
            GenerateTypeXSD(generalAsm);
            GenerateActionXSD(generalAsm);
        }

        private static void GenerateTypeXSD(Assembly[] generalAsm)
        {
            var types = generalAsm.SelectMany(a => a.GetTypes())
                .Where(t => t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).Any()).ToList();

            var categorizedTypes = types.Where(x => !string.IsNullOrEmpty(x.GetCustomAttribute<A_XSDTypeAttribute>()?.Category))
                .GroupBy(x => x.GetCustomAttribute<A_XSDTypeAttribute>()?.Category)
                .ToDictionary(g => g.Key ?? "Uncategorized", g => g.ToList());

            foreach (var category in categorizedTypes)
            {
                XmlSchema typechema = new XmlSchema()
                {
                    TargetNamespace = $"http://arctisaurora/Aurora{category.Key}Types",
                    ElementFormDefault = XmlSchemaForm.Qualified
                };
                typechema.Namespaces.Add("types", $"http://arctisaurora/Aurora{category.Key}Types");
                typechema.Namespaces.Add("xs", "http://www.w3.org/2001/XMLSchema");
                XmlSchemaSimpleTypeUnion categoryUnion = new XmlSchemaSimpleTypeUnion
                {
                    MemberTypes = category.Value
                        .Where(t => t.IsEnum)
                        .Select(t => new XmlQualifiedName("types:" + t.GetCustomAttribute<A_XSDTypeAttribute>()?.Name))
                        .ToArray()
                };
                if(categoryUnion.MemberTypes.Length != 0)
                {
                    XmlSchemaSimpleType typeSimpleCategory = new XmlSchemaSimpleType
                    {
                        Name = category.Key
                    };
                    typeSimpleCategory.Content = categoryUnion;
                    typechema.Items.Add(typeSimpleCategory);
                }
                foreach (var t in category.Value)
                {
                    if (t.IsEnum)
                    {
                        XmlSchemaSimpleType typeSimpleType = new XmlSchemaSimpleType
                        {
                            Name = t.GetCustomAttribute<A_XSDTypeAttribute>()?.Name
                        };
                        var typeRestriction = new XmlSchemaSimpleTypeRestriction
                        {
                            BaseTypeName = new XmlQualifiedName("xs:string")
                        };
                        var enumValues = Enum.GetNames(t);
                        foreach(var value in enumValues)
                        {
                            XmlSchemaEnumerationFacet enumElement = new XmlSchemaEnumerationFacet
                            {
                                Value = value
                            };
                            typeRestriction.Facets.Add(enumElement);
                        }
                        typeSimpleType.Content = typeRestriction;
                        typechema.Items.Add(typeSimpleType);
                    }
                    else
                    {
                        XmlSchemaComplexType typeComplexType = new XmlSchemaComplexType
                        {
                            Name = t.GetCustomAttribute<A_XSDTypeAttribute>()?.Name
                        };
                        var members = t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Where(m => (m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property) &&
                            m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).Any())
                            .Select(m => new
                            {
                                Member = m,
                                XmlAttribute = (A_XSDElementPropertyAttribute?)m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).FirstOrDefault()
                            }).ToList();
                        foreach (var member in members)
                        {
                            Type memberType = member.Member.MemberType == MemberTypes.Field
                                ? ((FieldInfo)member.Member).FieldType
                                : ((PropertyInfo)member.Member).PropertyType;
                            string typeName = MemberMap.ContainsKey(memberType) ? MemberMap[memberType] : typeMap[memberType];
                            XmlQualifiedName qualifiedName = new XmlQualifiedName(typeName);
                            XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                            {
                                Name = member.XmlAttribute?.Name ?? member.Member.Name,
                                SchemaTypeName = qualifiedName
                            };
                            typeComplexType.Attributes.Add(schemaAttribute);
                        }
                        typechema.Items.Add(typeComplexType);

                    }
                }
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = System.Text.Encoding.UTF8
                };
                string path = Paths.XMLSCHEMAS + $"\\{category.Key}TypeSchema.xsd";
                using (var writer = XmlWriter.Create(path, settings))
                {
                    typechema.Write(writer);
                }
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

            XmlSchemaSimpleType uncategorizedActions = new XmlSchemaSimpleType()
            {
                Name = "Uncategorized"
            };
            XmlSchemaSimpleTypeUnion allActionsUnion = new XmlSchemaSimpleTypeUnion()
            {
                MemberTypes = categorizedMethods.Keys
                    .Select(k => new XmlQualifiedName(k, "http://arctisaurora/ActionDependencies"))
                    .Union(globalMethods
                        .Select(m => new XmlQualifiedName(m.Attribute.Name, "http://arctisaurora/ActionDependencies")))
                    .ToArray()
            };
            uncategorizedActions.Content = allActionsUnion;
            actionSchema.Items.Add(uncategorizedActions);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = System.Text.Encoding.UTF8
            };

            // Write schema to file
            string path = Paths.XMLSCHEMAS + "\\actionSchema.xsd";
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

        private static Dictionary<Type, string> BuildTypeMap()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            var types = generalAsm.SelectMany(asm => asm.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_XSDTypeAttribute)t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).First()
                })).ToList();
            Dictionary<Type, string> map = new Dictionary<Type, string>();
            foreach (var type in types)
            {
                map[type.Type] = type.Attribute.Name;
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