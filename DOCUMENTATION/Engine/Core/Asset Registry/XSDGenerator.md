---
date:
Status: Current
tags:
  - Engine
  - d_XSD
  - d_UI
  - d_Data
  - d_XML
  - d_Filing
  - d_Input
cssclasses:
Linker:
  - "[[Arctis Aurora]]"
Class:
  - "[[XSDGenerator]]"
Parent Class:
Interfaces:
Type:
  - Public
  - Static
Attributes:
---
## Description
A general XSD generator for Aurora engine XML data types. It generates an XSD to the given parameters that can be used to write XML code for other systems. Basically a data-type rule-set generator.

Regeneration is incremental: each schema's inputs are fingerprinted, hashed, and recorded in `SchemaManifest.xml`; unchanged schemas are skipped (the `[XSD] Skipping … unchanged` log lines). The file also defines `AnyXMLType` (string ↔ `Type` resolution via `typeMap` + `FindType`) and the `IXMLParser<T>` parse contract.

## Input/Output

- public static void `GenerateXSD()` -> reflects over all assemblies and writes the schemas to `Paths.XMLSCHEMAS`. Tagged `[A_XSDActionDependency("XSDGenerator.GenerateXSD", "Bootstrap")]`, and also called directly at startup.
- The file also defines `IXMLParser<T>` (`static abstract T ParseXML(string)`) — the parse contract implemented by [[Asset Registries]], [[INPUT|InputHandler]], [[Vulkan Control]].

## Fields & Properties

```C#
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
```

## Methods / Functions

### Public

#### Generate XSD
General Assembly = Get Assemblies()
[[#Generate Dependency XSD]] (General Assembly)
### Private

#### Generate Dependency XSD
[[#Generate Type XSD]] (General Assembly)
[[#Generate Action XSD]] (General Assembly)

#### Generate Type XSD
[[#Generate Types Per Category]] (General Assembly)
[[#Generate All Types XSD]] (General Assembly)

#### Generate Action XSD
##### (General assembly)
get `all methods` from `general assembly` that implement [[A_XSDActionDependencyAttribute]]
get `global methods` from `all methods` that are `uncategorized`
get `categorized methods` from `all methods`
`all action schema`
add `actions` namespace
add `xs` namespace
for each `category` in `categorized methods`
	xml `simple type` (`category key`)
	xml `restriction`
	for each `method` in `category`
		`enumeration facet` (`method` name)
		add `enumeration facet` to `restriction`
	add `restriction` to `simple type`
	add `simple type` to `schema`
for each `method` in `global methods`
	xml `simple type` (action name)
	xml `restriction`
	xml `enumeration facet` (`method.name`)
	add `enumeration facet` to `restriction`
	add `restriction` to `simple type`
xml `uncategorized simple type`
xml `union`.`members` = `global methods` & `categorized methods`
add `union` to `uncategorized simple type`
add `uncategorized simple type` to `schema`
write `schema`

#### Generate Types Per Category
get all `types` that implement [[A_XSDTypeAttribute]] and group by `category`
for each `category` in `categories`
	`type schema` = [[#Build Schema Base]]
	xml simple type union where `member types` are `types` of `category`
	if simple type union `members` length `is > 0`
		xml schema simple type `type schema` (name)
		add `simple type union` to schema `simple type`
		add `simple type` to `type schema` 
	for each `type` in `category`
		if `type` is enum
			[[#Generate Enum Type]]
		else
			xml `schema element` (name)
			add `schema element` to `type schema`
			if `category` is categorized
				[[#Generate Complex Type]]
	if categorized
		write `sub type` xml schema

#### Generate All Types XSD
Xml schema `all type schema` = [[#Build Schema Base]]
schema simple type `all types type`
schema simple type `all types restriction` (string)
add restriction to `all types type`
add `all types type` to `schema`
get all `types` and `categorize` of that implement [[A_XSDTypeAttribute]]
for each `category` in `categories`
	xml schema simple type restriction (string)
	for each `type` in `category`
		type element (type name)
		add to `category restriction`
		add to `all types restriction`
	if `category` `uncategorized`
		continue
	else
		`all type schema` add sub category of `category`
for each `type` in `member types`
	xml schema enumeration facet (`type`)
	add `type` to `all types restriction`
settings for `all types`
write `all types`

## Helpers

#### Build Schema Base
##### (Type, name, schema)
`schema` (namespace)
namespace add `category types`
namespace add `xs`
namespace add `actions`
include `action` dependency
if `category` is not `Uncategorized`
	namespace add `all types`
	include `all types` dependency
return `schema`

#### Generate Enum Type 
##### (Type, Attribute, Assembly)
simple type restriction (string)
for each `value` in `Enum`
	restriction facet (`value`)
`simple type` content = restriction
`schema` add `simple type`

#### Generate Complex Type
##### (Type, Attribute, Schema, Assembly)
schema `complex type` (name)
schema `sequence`
for each `member` in [[#Get Annotated Members]]
	`member type`
	member xsd documentation
	if `member` is `list`
		`type name` = [[#Resolve Type Name]]
		qualified name = `type name`
		schema `element` (list element name, minOccurs, maxOccurs)
		sequence add list `element`
	else
		`type name` = [[#Resolve Type Name]]
		qualified name (`type name`)
		schema `attribute`
		`complex type` add `attribute`
if `Attribute` allows children
	xml schema `choice`
	`children` = `types` from `assembly` that are of type `Attribute.allowedType`
	for each `child` in `children`
		`name` = `child`.Attribute.Name
		if `name` is `empty`
			continue
		 schema `element` (name)
		 `choice` add `element`
	 `sequence` add `choice`
 `complex type` add `sequence`
 `schema` add `complex type`

#### Get Annotated Members
##### (Type)
return all `members` of [[A_XSDElementPropertyAttribute]] from `Type`

#### Resolve Type Name
##### (Member Type, Element Property Attribute)
`type` = `Nullable.GetUnderlyingType(Member Type)` ?? `Member Type`
if `type` is in `MemberMap` (out var `mapped`)
	if `mapped` == `action`
		return `actions:[Category]`
	if `mapped` == `types:Uncategorized`
		return `allTypes:[Category]`
	return `mapped`
if `type` is in `typeMap` (out var `mapped`)
	return `types:[mapped]`
return `xs:string`

#### Write Schema
##### (Schema, file name)
`path` = `Paths.XMLSchemas` + `file name`
writer (path, settings)
write schema

#### Build Type Map
##### ()
`general assembly`
find `types` of [[A_XSDTypeAttribute]] from assembly
dictionary `map`
for each `type` in `types`
	add `type` to `map`
return `map`