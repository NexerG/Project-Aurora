using ArctisAurora.EngineWork.Serialization;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace ArctisAurora.Core.AssetRegistry
{
    #region Attributes
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class A_XSDElementPropertyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";

        public A_XSDElementPropertyAttribute(string name, string? category = "Uncategorized", string? description = "")
        {
            Name = name;
            Description = description;
            Category = category;
        }
    }

    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
    public class A_XSDTypeAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public string PatternValue { get; set; } = string.Empty;
        public Type? AllowedChildren { get; set; } = null;
        public int MinChildren { get; set; } = 0;
        public int MaxChildren { get; set; } = -1;

        public A_XSDTypeAttribute(string name, string category = "Uncategorized", Type allowedChildren = null, int minChildren = 0, int maxChildren = -1, string patternValue="", string description = "")
        {
            Name = name;
            Description = description;
            Category = category;
            PatternValue = patternValue;
            AllowedChildren = allowedChildren;
            MaxChildren = maxChildren;
            MinChildren = minChildren;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class A_XSDActionDependencyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public A_XSDActionDependencyAttribute(string name, string? category = "Uncategorized", string? description = "")
        {
            Name = name;
            Description = description;
            Category = category;
        }
    }
    #endregion

    [A_XSDType("IParseableTypes", category:"Registry")]
    public interface IXMLParser<T> where T: class
    {
        public static abstract T ParseXML(string xmlName);
    }

    public sealed class AnyXMLType
    {
        public static readonly Dictionary<string, Type> typeMap = BuildTypeMap();
        private static Dictionary<string, Type> BuildTypeMap()
        {
            Dictionary<string, Type> map = new Dictionary<string, Type>();
            map.Add("xs:string", typeof(string));
            map.Add("xs:int", typeof(int));
            map.Add("xs:float", typeof(float));
            map.Add("xs:double", typeof(double));
            map.Add("xs:boolean", typeof(bool));
            map.Add("xs:byte", typeof(byte));
            map.Add("xs:short", typeof(short));
            map.Add("xs:long", typeof(long));
            map.Add("xs:unsignedInt", typeof(uint));
            map.Add("xs:unsignedShort", typeof(ushort));
            map.Add("xs:unsignedLong", typeof(ulong));
            map.Add("xs:char", typeof(string));
            map.Add("xs:decimal", typeof(decimal));
            map.Add("Action", typeof(Action));
            map.Add("Type", typeof(Type));
            map.Add("Object", typeof(object));

            return map;
        }

        public static Type? FindType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var types = assembly.GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).Any())
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = (A_XSDTypeAttribute)t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).First()
                    }).ToList();

                type = types.FirstOrDefault(t => t.Attribute.Name == typeName)?.Type;

                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }
    }

    public static class XSDGenerator
    {
        #region quick access
        // dictionaries
        public static readonly Dictionary<Type, string> typeMap = BuildTypeMap();
        public static readonly Dictionary<Type, string> MemberMap = new Dictionary<Type, string>
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
            { typeof(object), "Object" },
            { typeof(Action), "Action" },
            { typeof(Type), "Type" },
            { typeof(AnyXMLType), "types:Uncategorized" }
        };

        // static variables that all schemas use
        private static XmlSchemaImport actionDependency = new XmlSchemaImport
        {
            Namespace = "http://arctisaurora/ActionDependencies",
            SchemaLocation = "actionSchema.xsd"
        };
        private static XmlSchemaImport allTypeDependency = new XmlSchemaImport
        {
            Namespace = "http://arctisaurora/AuroraTypes",
            SchemaLocation = "AllTypesSchema.xsd"
        };

        private static XmlWriterSettings settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = System.Text.Encoding.UTF8,
        };

        #endregion

        public static void GenerateXSD()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            GenerateDependencyXSD(generalAsm);
        }

        private static void GenerateDependencyXSD(Assembly[] generalAsm)
        {
            GenerateTypeXSD(generalAsm);
            GenerateActionXSD(generalAsm);
        }

        private static void GenerateTypeXSD(Assembly[] generalAsm)
        {
            GenerateTypesPerCategory(generalAsm);
            GenerateAllTypesXSD(generalAsm);
        }

        private static void GenerateTypesPerCategory(Assembly[] generalAsm)
        {
            var types = generalAsm.SelectMany(asm => asm.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_XSDTypeAttribute)t.GetCustomAttributes(typeof(A_XSDTypeAttribute), true).First()
                }))
                .Where(x => x.Attribute != null).ToList();

            var categorizedTypes = types.Where(x => x.Attribute.Category != "Uncategorized")
                .GroupBy(x => x.Attribute.Category)
                .ToDictionary(g => g.Key ?? "Uncategorized", g => g.ToList());

            foreach (var category in categorizedTypes)
            {
                XmlSchema typeSchema = BuildSchemaBase(category.Key);

                XmlSchemaSimpleTypeUnion categoryUnion = new XmlSchemaSimpleTypeUnion
                {
                    MemberTypes = category.Value
                        .Where(t => t.Type.IsEnum)
                        .Select(t => new XmlQualifiedName("types:" + t.Attribute?.Name))
                        .ToArray()
                };
                if (categoryUnion.MemberTypes.Length != 0)
                {
                    XmlSchemaSimpleType typeSimpleCategory = new XmlSchemaSimpleType
                    {
                        Name = category.Key
                    };
                    typeSimpleCategory.Content = categoryUnion;
                    typeSchema.Items.Add(typeSimpleCategory);
                }
                foreach (var t in category.Value)
                {
                    if (t.Type.IsEnum)
                    {
                        GenerateEnumType(t.Type, t.Attribute.Name, typeSchema);
                    }
                    else
                    {
                        XmlSchemaElement schemaElement = new XmlSchemaElement
                        {
                            Name = t.Attribute.Name,
                            SchemaTypeName = new XmlQualifiedName($"types:{t.Attribute.Name}")
                        };
                        typeSchema.Items.Add(schemaElement);

                        if(category.Key != "Uncategorized")
                            GenerateComplexType(t.Type, t.Attribute, typeSchema, generalAsm);

                    }
                }
                if (category.Key != "Uncategorized")
                    WriteSchema(typeSchema, $"{category.Key}TypeSchema.xsd");
            }
        }

        private static void GenerateAllTypesXSD(Assembly[] generalAsm)
        {
            XmlSchema allTypeSchema = BuildSchemaBase("");

            XmlSchemaSimpleType allTypesType = new XmlSchemaSimpleType
            {
                Name = "Uncategorized"
            };
            XmlSchemaSimpleTypeRestriction allTypesRestriction = new XmlSchemaSimpleTypeRestriction
            {
                BaseTypeName = new XmlQualifiedName("xs:string")
            };
            allTypesType.Content = allTypesRestriction;
            allTypeSchema.Items.Add(allTypesType);

            var types = generalAsm.SelectMany(a => a.GetTypes())
                .Where(t => t.GetCustomAttributes(typeof(A_XSDTypeAttribute), false).Any()).ToList();

            var categorizedTypes = types.Where(x => !string.IsNullOrEmpty(x.GetCustomAttribute<A_XSDTypeAttribute>()?.Category))
                .GroupBy(x => x.GetCustomAttribute<A_XSDTypeAttribute>()?.Category)
                .ToDictionary(g => g.Key ?? "Uncategorized", g => g.ToList());

            foreach (var category in categorizedTypes)
            {
                XmlSchemaSimpleTypeRestriction categoryRestriction = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = new XmlQualifiedName("xs:string")
                };

                foreach (var t in category.Value)
                {
                    XmlSchemaEnumerationFacet typeElement = new XmlSchemaEnumerationFacet
                    {
                        Value = t.GetCustomAttribute<A_XSDTypeAttribute>()?.Name ?? t.Name
                    };
                    categoryRestriction.Facets.Add(typeElement);
                    allTypesRestriction.Facets.Add(typeElement);
                }
                if (category.Key == "Uncategorized")
                    continue;
                allTypeSchema.Items.Add(new XmlSchemaSimpleType
                {
                    Name = category.Key,
                    Content = categoryRestriction
                });
            }

            foreach(var type in MemberMap.Values)
            {
                XmlSchemaEnumerationFacet typeElement = new XmlSchemaEnumerationFacet
                {
                    Value = type
                };
                allTypesRestriction.Facets.Add(typeElement);
            }

            WriteSchema(allTypeSchema, "AllTypesSchema.xsd");
        }

        private static void GenerateActionXSD(Assembly[] generalAsm)
        {
            var allMethods = generalAsm.SelectMany(a => a.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Select(m => new
                {
                    Method = m,
                    DeclaringType = t,
                    Attribute = m.GetCustomAttributes(typeof(A_XSDActionDependencyAttribute), true)
                                    .FirstOrDefault() as A_XSDActionDependencyAttribute
                })
                .Where(x => x.Attribute != null))).ToList();

            var globalMethods = allMethods.Where(x => x.Attribute.Category == "Uncategorized").ToList();

            var categorizedMethods = allMethods.Where(x => x.Attribute.Category != "Uncategorized")
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

            // Write schema to file
            WriteSchema(actionSchema, "actionSchema.xsd");
        }

        private static XmlSchema BuildSchemaBase(string category)
        {
            XmlSchema schema = new XmlSchema
            {
                TargetNamespace = $"http://arctisaurora/Aurora{category}Types",
                ElementFormDefault = XmlSchemaForm.Qualified
            };
            schema.Namespaces.Add("types", $"http://arctisaurora/Aurora{category}Types");
            schema.Namespaces.Add("xs", "http://www.w3.org/2001/XMLSchema");
            schema.Namespaces.Add("actions", "http://arctisaurora/ActionDependencies");
            schema.Includes.Add(actionDependency);

            if (category != "")
            {
                schema.Namespaces.Add("allTypes", "http://arctisaurora/AuroraTypes");
                schema.Includes.Add(allTypeDependency);
            }
            return schema;
        }

        private static void GenerateEnumType(Type t, string name, XmlSchema schema)
        {
            XmlSchemaSimpleTypeRestriction restriction = new()
            {
                BaseTypeName = new XmlQualifiedName("xs:string")
            };
            foreach (var value in Enum.GetNames(t))
            {
                restriction.Facets.Add(new XmlSchemaEnumerationFacet { Value = value });
            }

            schema.Items.Add(new XmlSchemaSimpleType
            {
                Name = name,
                Content = restriction
            });
        }

        private static void GenerateComplexType(Type t, A_XSDTypeAttribute attribute, XmlSchema schema, Assembly[] generalAsm)
        {
            XmlSchemaComplexType complexType = new XmlSchemaComplexType()
            {
                Name = attribute.Name
            };
            XmlSchemaSequence sequence = new XmlSchemaSequence();

            foreach (var member in GetAnnotatedMembers(t))
            {
                Type memberType = member.Member.MemberType == MemberTypes.Field
                    ? ((FieldInfo)member.Member).FieldType
                    : ((PropertyInfo)member.Member).PropertyType;

                var annotation = new XmlSchemaAnnotation();
                var documentation = new XmlSchemaDocumentation();
                documentation.Markup = new XmlNode[]
                {
                                new XmlDocument().CreateTextNode(member.XmlAttribute?.Description ?? "")
                };
                annotation.Items.Add(documentation);

                if (memberType.IsGenericType && typeof(IEnumerable<>).MakeGenericType(memberType.GetGenericArguments()).IsAssignableFrom(memberType))
                {
                    string typeName = ResolveTypeName(memberType.GetGenericArguments()[0], member.XmlAttribute);

                    XmlQualifiedName qualifiedName = new(typeName);
                    XmlSchemaElement listElement = new()
                    {
                        Name = member.XmlAttribute?.Name ?? member.Member.Name,
                        SchemaTypeName = qualifiedName,
                        MinOccurs = 0,
                        MaxOccursString = "unbounded"
                    };
                    listElement.Annotation = annotation;
                    sequence.Items.Add(listElement);
                }
                else
                {
                    string typeName = ResolveTypeName(memberType, member.XmlAttribute);

                    XmlQualifiedName qualifiedName = new XmlQualifiedName(typeName);
                    XmlSchemaAttribute schemaAttribute = new XmlSchemaAttribute
                    {
                        Name = member.XmlAttribute?.Name ?? member.Member.Name,
                        SchemaTypeName = qualifiedName
                    };
                    complexType.Attributes.Add(schemaAttribute);
                }
            }

            if (attribute.AllowedChildren != null)
            {
                XmlSchemaChoice childChoice = new XmlSchemaChoice
                {
                    MinOccurs = attribute.MinChildren,
                    MaxOccursString = attribute.MaxChildren == -1 ? "unbounded" : attribute.MaxChildren.ToString()
                };

                var children = generalAsm.SelectMany(a => a.GetTypes()
                    .Where(ty => attribute.AllowedChildren.IsAssignableFrom(ty)
                        && ty != attribute.AllowedChildren)).ToList();
                foreach (var child in children)
                {
                    string childName = child.GetCustomAttribute<A_XSDTypeAttribute>(false)?.Name ?? string.Empty;
                    if (childName == string.Empty)
                    {
                        continue;
                    }
                    XmlSchemaElement childElement = new XmlSchemaElement
                    {
                        Name = childName,
                        SchemaTypeName = new XmlQualifiedName($"types:{childName}")
                    };
                    childChoice.Items.Add(childElement);
                }
                sequence.Items.Add(childChoice);
            }

            complexType.Particle = sequence;
            schema.Items.Add(complexType);
        }

        private static List<(MemberInfo Member, A_XSDElementPropertyAttribute? XmlAttribute)> GetAnnotatedMembers(Type type)
        {
            return type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => (m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property) &&
                            m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).Any())
                .Select(m => (
                    Member: m,
                    XmlAttribute: (A_XSDElementPropertyAttribute?)m.GetCustomAttributes(typeof(A_XSDElementPropertyAttribute), true).FirstOrDefault()
                )).ToList();
        }
        
        private static string ResolveTypeName(Type memberType, A_XSDElementPropertyAttribute? xmlAttr)
        {
            Type resolved = Nullable.GetUnderlyingType(memberType) ?? memberType;

            if (MemberMap.TryGetValue(resolved, out string? mapped))
            {
                if (mapped == "Action") return $"actions:{xmlAttr?.Category}";
                if (mapped == "types:Uncategorized") return $"allTypes:{xmlAttr?.Category}";
                return mapped;
            }

            if (typeMap.TryGetValue(resolved, out string? typeMapped))
                return $"types:{typeMapped}";

            return "xs:string"; // fallback
        }

        private static void WriteSchema(XmlSchema schema, string fileName)
        {
            string path = Path.Combine(Paths.XMLSCHEMAS, fileName);
            using var writer = XmlWriter.Create(path, settings);
            schema.Write(writer);
        }

        #region MapBuilders
        private static Dictionary<Type, string> BuildTypeMap()
        {
            var generalAsm = AppDomain.CurrentDomain.GetAssemblies();
            var types = generalAsm.SelectMany(asm => asm.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(A_XSDTypeAttribute), true).Any())
                .Select(t => new
                {
                    Type = t,
                    Attribute = (A_XSDTypeAttribute)t.GetCustomAttributes(typeof(A_XSDTypeAttribute), true).First()
                })).ToList();
            Dictionary<Type, string> map = new Dictionary<Type, string>();
            /*{
                { typeof(string),     "xs:string" },
                { typeof(int),        "xs:int" },
                { typeof(float),      "xs:float" },
                { typeof(double),     "xs:double" },
                { typeof(bool),       "xs:boolean" },
                { typeof(byte),       "xs:byte" },
                { typeof(short),      "xs:short" },
                { typeof(long),       "xs:long" },
                { typeof(uint),       "xs:unsignedInt" },
                { typeof(ushort),     "xs:unsignedShort" },
                { typeof(ulong),      "xs:unsignedLong" },
                { typeof(char),       "xs:string" },
                { typeof(decimal),    "xs:decimal" },
                { typeof(object),     "Object" },
                { typeof(Action),     "Action" },
                { typeof(Type),       "Type" },
                { typeof(AnyXMLType), "types:Uncategorized" }
            };*/
            foreach (var type in types)
            {
                map[type.Type] = type.Attribute.Name;
            }
            return map;
        }
        #endregion
    }
}