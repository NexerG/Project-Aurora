namespace ArctisAurora.Core.Security
{
    // Keys for encrypted files: remembered for the session when asked, otherwise handed to the next load once.
    public static class FileKeys
    {
        private static readonly Dictionary<string, PasswordKey> remembered = new Dictionary<string, PasswordKey>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PasswordKey> once = new Dictionary<string, PasswordKey>(StringComparer.OrdinalIgnoreCase);

        public static void Use(string path, PasswordKey key, bool remember)
        {
            path = Path.GetFullPath(path);
            if (remember) remembered[path] = key;
            else once[path] = key;
        }

        public static PasswordKey? Take(string path)
        {
            path = Path.GetFullPath(path);
            if (remembered.TryGetValue(path, out PasswordKey? key)) return key;
            return once.Remove(path, out key) ? key : null;
        }

        public static bool Has(string path)
        {
            path = Path.GetFullPath(path);
            return remembered.ContainsKey(path) || once.ContainsKey(path);
        }

        public static bool IsRemembered(PasswordKey key) => remembered.ContainsValue(key);

        public static void Renamed(string from, string to)
        {
            from = Path.GetFullPath(from);
            to = Path.GetFullPath(to);
            if (remembered.Remove(from, out PasswordKey? key)) remembered[to] = key;
            if (once.Remove(from, out key)) once[to] = key;
        }

        // Zeroes and drops every key.
        public static void Clear()
        {
            foreach (PasswordKey key in remembered.Values.Concat(once.Values)) key.Dispose();
            remembered.Clear();
            once.Clear();
        }
    }
}
