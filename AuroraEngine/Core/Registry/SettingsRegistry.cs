using ArctisAurora.Core.Filing.Serialization;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.Registry
{
    // Marker for a settings group; the allowedChildren target the schema generator scans.
    public interface ISettingsGroup { }

    // A group whose stored shape can go stale. Migrate rewrites the stored element in place, before
    // anything on it is read.
    public interface IMigratableSettings : ISettingsGroup
    {
        int version { get; }
        void Migrate(int from, XElement stored);
    }

    [A_XSDType("SettingsManifest", "Settings", allowedChildren: typeof(ISettingsGroup))]
    public class SettingsManifest { }

    // One instance per ISettingsGroup, found by reflection, filled from every Data/XML/Settings
    // manifest across the mounts and then from the application's write root.
    public static class SettingsRegistry
    {
        private const string settingsDir = "XML/Settings";
        private const string versionAttribute = "SettingsVersion";

        // groups, the values they held before the write root was applied, and what the write root said
        private static readonly Dictionary<Type, ISettingsGroup> groups = new Dictionary<Type, ISettingsGroup>();
        private static readonly Dictionary<Type, MemberSnapshot> baselines = new Dictionary<Type, MemberSnapshot>();
        private static readonly Dictionary<Type, MemberSnapshot> defaultSnapshots = new Dictionary<Type, MemberSnapshot>();
        private static readonly Dictionary<Type, XElement> stored = new Dictionary<Type, XElement>();

        private static string writeRoot;

        public static IReadOnlyDictionary<Type, ISettingsGroup> Groups => groups;

        // Folder the application's own settings are read from last and written to.
        public static void SetWriteRoot(string path) => writeRoot = path;

        public static T Get<T>() where T : ISettingsGroup
        {
            if (groups.TryGetValue(typeof(T), out ISettingsGroup group))
                return (T)group;
            throw new Exception($"{typeof(T).Name} is not a registered settings group.");
        }

        [A_XSDActionDependency("Settings.LoadAll", "Bootstrap")]
        public static void LoadAll()
        {
            groups.Clear();
            baselines.Clear();
            stored.Clear();

            foreach (Type type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(t => !t.IsAbstract && !t.IsInterface
                            && typeof(ISettingsGroup).IsAssignableFrom(t)
                            && t.GetCustomAttribute<A_XSDTypeAttribute>(false) != null))
                groups[type] = (ISettingsGroup)Activator.CreateInstance(type);

            IReadOnlyList<IDataMount> mounts = VirtualFileSystem.Mounts;
            for (int i = mounts.Count - 1; i >= 0; i--)
                foreach (string file in mounts[i].Enumerate(settingsDir, "*.xml").OrderBy(f => f))
                    ApplyFile(file, false);

            foreach (KeyValuePair<Type, ISettingsGroup> group in groups)
                baselines[group.Key] = Snapshot(group.Value);

            if (writeRoot != null && Directory.Exists(writeRoot))
                foreach (string file in Directory.GetFiles(writeRoot, "*.xml").OrderBy(f => f))
                    ApplyFile(file, true);
        }

        public static void Save<T>() where T : ISettingsGroup => Save(typeof(T));

        public static void SaveAll()
        {
            foreach (Type type in groups.Keys.ToList())
                Save(type);
        }

        #region ---- read ----
        private static void ApplyFile(string path, bool fromWriteRoot)
        {
            XElement root = XElement.Load(path);
            HashSet<(object, FieldInfo)> clearedLists = new HashSet<(object, FieldInfo)>();

            foreach (XElement element in root.Elements())
            {
                Type type = AnyXMLType.FindType(element.Name.LocalName);
                if (type == null || !groups.TryGetValue(type, out ISettingsGroup group))
                {
                    Console.WriteLine($"[Settings] '{element.Name.LocalName}' in {Path.GetFileName(path)} is not a settings group — skipping.");
                    continue;
                }

                Migrate(element, group);
                ApplyInto(element, group, clearedLists);
                if (fromWriteRoot) stored[type] = element;
            }
        }

        // Only a stamped element migrates, so a shipped manifest is always read as current.
        private static void Migrate(XElement element, ISettingsGroup group)
        {
            if (group is not IMigratableSettings migratable) return;

            XAttribute stamp = element.Attribute(versionAttribute);
            if (stamp == null || !int.TryParse(stamp.Value, out int from) || from >= migratable.version) return;

            Console.WriteLine($"[Settings] migrating {group.GetType().Name} from version {from} to {migratable.version}.");
            migratable.Migrate(from, element);
            element.SetAttributeValue(versionAttribute, migratable.version);
        }

        private static void ApplyInto(XElement element, object target, HashSet<(object, FieldInfo)> clearedLists)
        {
            XmlReflection.ApplyAttributes(element, target, true);

            foreach (XElement child in element.Elements())
            {
                Type type = AnyXMLType.FindType(child.Name.LocalName);
                if (type == null) continue;

                MemberInfo member = XmlReflection.ComplexMembers(target.GetType())
                    .FirstOrDefault(m => XmlReflection.MemberType(m).IsAssignableFrom(type));
                if (member != null)
                {
                    object nested = XmlReflection.GetMember(member, target)
                        ?? Activator.CreateInstance(XmlReflection.MemberType(member));
                    XmlReflection.SetMember(member, target, nested);
                    ApplyInto(child, nested, clearedLists);
                    continue;
                }

                FieldInfo list = XmlReflection.ChildListFields(target.GetType())
                    .FirstOrDefault(f => f.FieldType.GetGenericArguments()[0].IsAssignableFrom(type));
                if (list == null) continue;

                IList entries = (IList)list.GetValue(target);
                if (clearedLists.Add((target, list))) entries.Clear();

                object entry = Activator.CreateInstance(type);
                ApplyInto(child, entry, clearedLists);
                entries.Add(entry);
            }
        }
        #endregion

        #region ---- write ----
        private static void Save(Type type)
        {
            if (writeRoot == null)
                throw new Exception("SettingsRegistry.SetWriteRoot has to be called before settings can be saved.");

            A_XSDTypeAttribute meta = type.GetCustomAttribute<A_XSDTypeAttribute>(false);
            XElement group = WriteDiff(groups[type], baselines[type]);

            if (groups[type] is IMigratableSettings migratable)
                group.SetAttributeValue(versionAttribute, migratable.version);
            if (stored.TryGetValue(type, out XElement previous))
                CarryUnknowns(group, previous, type);

            XNamespace ns = XSDGenerator.NamespaceFor("Settings");
            XElement root = new XElement(ns + "SettingsManifest", group);

            string schemaFile = Path.Combine(Paths.XMLSCHEMAS, "SettingsTypeSchema.xsd");
            string relative = Path.GetRelativePath(writeRoot, schemaFile).Replace('\\', '/');

            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            root.SetAttributeValue(XNamespace.Xmlns + "xsi", xsi.NamespaceName);
            root.SetAttributeValue(xsi + "schemaLocation", $"{ns.NamespaceName} {relative}");

            Directory.CreateDirectory(writeRoot);
            new XDocument(new XDeclaration("1.0", "utf-8", null), root)
                .Save(Path.Combine(writeRoot, meta.Name + ".xml"));
        }

        private static XElement WriteDiff(object node, MemberSnapshot baseline)
        {
            A_XSDTypeAttribute meta = node.GetType().GetCustomAttribute<A_XSDTypeAttribute>(false)
                ?? throw new Exception($"Type {node.GetType().Name} is missing [A_XSDType].");

            XNamespace ns = XSDGenerator.NamespaceFor(meta.Category);
            XElement element = new XElement(ns + meta.Name);

            foreach (MemberInfo member in XmlReflection.ScalarMembers(node.GetType()))
            {
                object value = XmlReflection.GetMember(member, node);
                if (value == null) continue;
                if (baseline.scalars.TryGetValue(member, out object previous) && Equals(value, previous)) continue;
                element.SetAttributeValue(member.GetCustomAttribute<A_XSDElementPropertyAttribute>().Name,
                    Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            foreach (MemberInfo member in XmlReflection.ComplexMembers(node.GetType()))
            {
                object value = XmlReflection.GetMember(member, node);
                if (value == null) continue;

                baseline.nested.TryGetValue(member, out MemberSnapshot nested);
                XElement child = WriteDiff(value, nested ?? new MemberSnapshot());
                if (child.HasAttributes || child.HasElements) element.Add(child);
            }

            foreach (FieldInfo list in XmlReflection.ChildListFields(node.GetType()))
            {
                List<object> entries = ((IEnumerable)list.GetValue(node)).Cast<object>().ToList();
                baseline.lists.TryGetValue(list, out List<MemberSnapshot> previous);
                if (!ListChanged(entries, previous)) continue;

                foreach (object entry in entries)
                    element.Add(WriteDiff(entry, DefaultsOf(entry.GetType())));
            }
            return element;
        }

        // Attributes the stored file carried that no member claims, copied onto the fresh diff so a
        // save cannot erase a value the code stopped knowing about. List entries are not walked.
        private static void CarryUnknowns(XElement written, XElement previous, Type type)
        {
            HashSet<string> known = new HashSet<string>(
                XmlReflection.ScalarMembers(type).Select(m => m.GetCustomAttribute<A_XSDElementPropertyAttribute>().Name),
                StringComparer.OrdinalIgnoreCase) { versionAttribute };

            foreach (XAttribute attribute in previous.Attributes())
                if (!attribute.IsNamespaceDeclaration
                    && !known.Contains(attribute.Name.LocalName)
                    && written.Attribute(attribute.Name) == null)
                    written.SetAttributeValue(attribute.Name, attribute.Value);

            foreach (MemberInfo member in XmlReflection.ComplexMembers(type))
            {
                Type memberType = XmlReflection.MemberType(member);
                A_XSDTypeAttribute meta = memberType.GetCustomAttribute<A_XSDTypeAttribute>(false);
                if (meta == null) continue;

                XElement previousChild = previous.Elements().FirstOrDefault(e => e.Name.LocalName == meta.Name);
                if (previousChild == null) continue;

                XElement writtenChild = written.Elements().FirstOrDefault(e => e.Name.LocalName == meta.Name);
                bool created = writtenChild == null;
                if (created)
                {
                    writtenChild = new XElement(XSDGenerator.NamespaceFor(meta.Category) + meta.Name);
                    written.Add(writtenChild);
                }

                CarryUnknowns(writtenChild, previousChild, memberType);
                if (created && !writtenChild.HasAttributes && !writtenChild.HasElements) writtenChild.Remove();
            }
        }

        private static bool ListChanged(List<object> entries, List<MemberSnapshot> baseline)
        {
            if (baseline == null || entries.Count != baseline.Count) return true;

            for (int i = 0; i < entries.Count; i++)
                foreach (MemberInfo member in XmlReflection.ScalarMembers(entries[i].GetType()))
                    if (!baseline[i].scalars.TryGetValue(member, out object previous)
                        || !Equals(XmlReflection.GetMember(member, entries[i]), previous))
                        return true;

            return false;
        }
        #endregion

        #region ---- snapshots ----
        // Every value a node carries, deep enough to diff a save against.
        private sealed class MemberSnapshot
        {
            public Dictionary<MemberInfo, object> scalars = new Dictionary<MemberInfo, object>();
            public Dictionary<MemberInfo, MemberSnapshot> nested = new Dictionary<MemberInfo, MemberSnapshot>();
            public Dictionary<FieldInfo, List<MemberSnapshot>> lists = new Dictionary<FieldInfo, List<MemberSnapshot>>();
        }

        private static MemberSnapshot Snapshot(object node)
        {
            MemberSnapshot snapshot = new MemberSnapshot();

            foreach (MemberInfo member in XmlReflection.ScalarMembers(node.GetType()))
                snapshot.scalars[member] = XmlReflection.GetMember(member, node);

            foreach (MemberInfo member in XmlReflection.ComplexMembers(node.GetType()))
            {
                object value = XmlReflection.GetMember(member, node);
                if (value != null) snapshot.nested[member] = Snapshot(value);
            }

            foreach (FieldInfo list in XmlReflection.ChildListFields(node.GetType()))
            {
                List<MemberSnapshot> entries = new List<MemberSnapshot>();
                foreach (object entry in (IEnumerable)list.GetValue(node))
                    entries.Add(Snapshot(entry));
                snapshot.lists[list] = entries;
            }
            return snapshot;
        }

        private static MemberSnapshot DefaultsOf(Type type)
        {
            if (!defaultSnapshots.TryGetValue(type, out MemberSnapshot snapshot))
                defaultSnapshots[type] = snapshot = Snapshot(Activator.CreateInstance(type));
            return snapshot;
        }
        #endregion
    }
}
