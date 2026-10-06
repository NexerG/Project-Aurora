using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.Filing.Serialization;
using System.Xml.Linq;

namespace ArctisAurora.Core.Tex
{
    // Liang's hyphenation: a trie of patterns and whole-word exceptions, over lowercase a-z.
    public sealed class TexHyphenator
    {
        private static readonly LogChannel Log = LogChannel.For("Tex");

        private sealed class Node
        {
            public Dictionary<char, Node>? next;
            public byte[]? levels;
        }

        private readonly Node root = new Node();
        private readonly Dictionary<string, bool[]> exceptions = new Dictionary<string, bool[]>();
        public readonly int leftMin;
        public readonly int rightMin;

        private static readonly Lazy<TexHyphenator?> english = new Lazy<TexHyphenator?>(() => Load(Paths.Doc("Hyphenation/en-us.hyphenation.xml")));

        // US English, or null when its pattern file is missing.
        public static TexHyphenator? English => english.Value;

        public TexHyphenator(IEnumerable<string> patterns, IEnumerable<string> exceptions, int leftMin = 2, int rightMin = 3)
        {
            this.leftMin = leftMin;
            this.rightMin = rightMin;
            foreach (string pattern in patterns)
                AddPattern(pattern);
            foreach (string exception in exceptions)
                if (Exception(exception) is (string word, bool[] points)) this.exceptions[word] = points;
        }

        // <Hyphenation LeftMin RightMin><Patterns/><Exceptions/></Hyphenation>, each list whitespace-separated.
        public static TexHyphenator? Load(string path)
        {
            if (!File.Exists(path))
            {
                Log.Warn($"no hyphenation patterns at {path}; LaTeX is not hyphenated");
                return null;
            }
            XElement root = XElement.Load(path);
            string[] List(string name) => (root.Element(name)?.Value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return new TexHyphenator(List("Patterns"), List("Exceptions"),
                (int?)root.Attribute("LeftMin") ?? 2, (int?)root.Attribute("RightMin") ?? 3);
        }

        // "as-so-ciate" → the word and a break before each letter that follows a hyphen.
        public static (string word, bool[] points)? Exception(string hyphenated)
        {
            string word = hyphenated.Replace("-", string.Empty).ToLowerInvariant();
            if (word.Length == 0) return null;
            bool[] points = new bool[word.Length + 1];
            int letters = 0;
            foreach (char c in hyphenated)
                if (c == '-') points[letters] = true;
                else letters++;
            return (word, points);
        }

        // "hy3ph": the letters along the trie, and the level of each gap between them.
        private void AddPattern(string pattern)
        {
            Node node = root;
            List<byte> levels = new List<byte> { 0 };
            foreach (char c in pattern)
            {
                if (c >= '0' && c <= '9')
                {
                    levels[^1] = (byte)(c - '0');
                    continue;
                }
                node.next ??= new Dictionary<char, Node>();
                if (!node.next.TryGetValue(c, out Node? child)) node.next[c] = child = new Node();
                node = child;
                levels.Add(0);
            }
            node.levels = levels.ToArray();
        }

        // A break allowed before letter i of a lowercase word, or null for none; extra exceptions win over the built-in ones.
        public bool[]? Points(string word, Dictionary<string, bool[]>? extra = null)
        {
            int n = word.Length;
            if (n < leftMin + rightMin) return null;

            bool[]? points = null;
            if (extra != null && extra.TryGetValue(word, out bool[]? given) || exceptions.TryGetValue(word, out given))
                points = (bool[])given.Clone();
            else
            {
                string w = "." + word + ".";
                byte[] values = new byte[w.Length + 1];
                for (int i = 0; i < w.Length; i++)
                {
                    Node node = root;
                    for (int j = i; j < w.Length; j++)
                    {
                        if (node.next == null || !node.next.TryGetValue(w[j], out Node? child)) break;
                        node = child;
                        if (node.levels is not byte[] levels) continue;
                        for (int k = 0; k < levels.Length; k++)
                            if (levels[k] > values[i + k]) values[i + k] = levels[k];
                    }
                }
                points = new bool[n + 1];
                for (int m = 1; m < n; m++)
                    points[m] = (values[m + 1] & 1) == 1;
            }

            bool any = false;
            for (int m = 0; m <= n; m++)
            {
                if (m < leftMin || m > n - rightMin) points[m] = false;
                any |= points[m];
            }
            return any ? points : null;
        }
    }
}
