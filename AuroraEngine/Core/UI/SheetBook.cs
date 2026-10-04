namespace ArctisAurora.Core.UI
{
    // Every sheet the vault has loaded, one copy per file, and the formula graph across them.
    public static class SheetBook
    {
        public static readonly SheetCalc calc = new SheetCalc();

        // a reference's file part to a full path; set by the host
        public static Func<string, string?>? findSheet;

        // the document whose cells were written, or null when only values may have moved
        public static event Action<SheetDocument?>? changed;

        private static readonly Dictionary<string, SheetDocument> documents = new Dictionary<string, SheetDocument>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<SheetPage, SheetDocument> owners = new Dictionary<SheetPage, SheetDocument>();

        // The loaded copy of a file, loading it on first use.
        public static SheetDocument Get(string path)
        {
            path = Path.GetFullPath(path);
            if (documents.TryGetValue(path, out SheetDocument? document)) return document;

            document = SheetDocument.Load(path);
            documents[path] = document;
            Register(document);
            return document;
        }

        // A document the graph should know, with or without a file; again after pages are added.
        public static void Register(SheetDocument document)
        {
            foreach (SheetPage page in document.pages)
                owners[page] = document;
            calc.Add(document);
        }

        public static SheetDocument? OwnerOf(SheetPage page) => owners.TryGetValue(page, out SheetDocument? document) ? document : null;

        // A reference's file part, or null when nothing in the vault answers to it.
        public static SheetDocument? Resolve(string file)
        {
            string? path = findSheet?.Invoke(file);
            return path != null && File.Exists(path) ? Get(path) : null;
        }

        // Cells written by an edit, an undo or a redo.
        public static void Changed(SheetDocument document, SheetPage page, IEnumerable<(int row, int column)> cells)
        {
            calc.Changed(page, cells);
            changed?.Invoke(document);
        }

        #region ---- vault ----
        // A sheet now exists that references may have been waiting for.
        public static void Created()
        {
            calc.RecalcAll();
            RaiseAll();
        }

        public static void Deleted(string path)
        {
            path = Path.GetFullPath(path);
            if (documents.Remove(path, out SheetDocument? document)) Unregister(document);
            calc.RecalcAll();
            RaiseAll();
        }

        public static void Unregister(SheetDocument document)
        {
            foreach (SheetPage page in document.pages)
                owners.Remove(page);
            calc.Remove(document);
        }

        // Rekeys the file and rewrites every reference to it, loaded or on disk.
        public static void Renamed(string oldPath, string newPath, IEnumerable<string> vaultSheets)
        {
            oldPath = Path.GetFullPath(oldPath);
            newPath = Path.GetFullPath(newPath);
            string oldName = BaseName(oldPath);
            string newName = BaseName(newPath);
            string oldStem = Stem(oldPath);

            if (documents.Remove(oldPath, out SheetDocument? moved))
            {
                moved.name = newName;
                documents[newPath] = moved;
            }

            foreach ((string path, SheetDocument document) in documents)
            {
                if (!Rewrite(document, oldStem, oldName, newName)) continue;
                if (TabViewControl.FindOpenDocument(path, out _) == null) document.Save(path);
                changed?.Invoke(document);
            }

            foreach (string file in vaultSheets)
            {
                string path = Path.GetFullPath(file);
                if (documents.ContainsKey(path) || !File.Exists(path)) continue;
                SheetDocument document = SheetDocument.Load(path);
                if (Rewrite(document, oldStem, oldName, newName)) document.Save(path);
            }

            calc.RecalcAll();
            RaiseAll();
        }

        // Drops every loaded sheet; for a vault switch.
        public static void Clear()
        {
            documents.Clear();
            owners.Clear();
            calc.Clear();
        }

        private static bool Rewrite(SheetDocument document, string oldStem, string oldName, string newName)
        {
            bool any = false;
            foreach (SheetPage page in document.pages)
                foreach (SheetLayer layer in page.layers)
                    foreach (SheetCell cell in layer.cells.Values)
                    {
                        if (!SheetFormula.IsFormula(cell.raw)) continue;
                        string rewritten = SheetFormula.RenameFile(cell.raw, file => Names(file, oldStem) ? Renamed(file, oldName, newName) : null);
                        if (rewritten == cell.raw) continue;
                        cell.raw = rewritten;
                        any = true;
                    }
            return any;
        }

        // A file part names a path when it is the file's name or the end of its path, as vault links resolve.
        private static bool Names(string file, string stem)
        {
            string name = file.Trim().Replace('\\', '/');
            if (name.EndsWith(SheetDocument.extension, StringComparison.OrdinalIgnoreCase))
                name = name[..^SheetDocument.extension.Length];
            return stem.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Renamed(string file, string oldName, string newName)
        {
            string trimmed = file.Trim();
            int cut = trimmed.EndsWith(SheetDocument.extension, StringComparison.OrdinalIgnoreCase)
                ? trimmed.Length - SheetDocument.extension.Length
                : trimmed.Length;
            return trimmed[..(cut - oldName.Length)] + newName + trimmed[cut..];
        }

        private static string BaseName(string path) => Path.GetFileName(path)[..^SheetDocument.extension.Length];

        private static string Stem(string path) => path.Replace('\\', '/')[..^SheetDocument.extension.Length];

        private static void RaiseAll() => changed?.Invoke(null);
        #endregion
    }
}
