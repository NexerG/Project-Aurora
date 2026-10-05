namespace ArctisAurora.Core.UI
{
    // Every sheet the vault has loaded, one copy per file, and the formula graph across them.
    public static class SheetBook
    {
        public static readonly SheetCalc calc = new SheetCalc();

        // vault lookups; set by the host
        public static Func<string, string?>? findSheet;
        public static Func<IEnumerable<string>>? vaultSheets;
        public static Func<IEnumerable<string>>? vaultNotes;

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

        // Pages or layers of a document were added, removed or shown.
        public static void Restructured(SheetDocument document)
        {
            List<SheetPage> gone = new List<SheetPage>();
            foreach ((SheetPage page, SheetDocument owner) in owners)
                if (ReferenceEquals(owner, document) && !document.pages.Contains(page)) gone.Add(page);
            foreach (SheetPage page in gone)
                owners.Remove(page);
            foreach (SheetPage page in document.pages)
                owners[page] = document;

            calc.RecalcAll();
            changed?.Invoke(document);
        }

        // Rewrites every reference to a page of this document, loaded, on disk or in notes; the page is already renamed.
        public static void PageRenamed(SheetDocument document, string oldName, string newName)
        {
            string? home = null;
            foreach ((string path, SheetDocument loaded) in documents)
                if (ReferenceEquals(loaded, document)) home = path;

            Func<string?, string, string?> RenameIn(SheetDocument owner) => (file, page) =>
                page.Equals(oldName, StringComparison.OrdinalIgnoreCase)
                && (file == null ? ReferenceEquals(owner, document) : home != null && Names(file, home))
                    ? newName : null;

            RewritePages(document, RenameIn(document));
            foreach ((string path, SheetDocument other) in documents)
            {
                if (ReferenceEquals(other, document) || other.isCsv || !RewritePages(other, RenameIn(other))) continue;
                if (TabViewControl.FindOpenDocument(path, out _) == null) other.Save(path);
                changed?.Invoke(other);
            }

            if (home != null)
            {
                foreach (string file in vaultSheets?.Invoke() ?? Enumerable.Empty<string>())
                {
                    string path = Path.GetFullPath(file);
                    if (documents.ContainsKey(path) || !File.Exists(path)) continue;
                    SheetDocument other = SheetDocument.Load(path);
                    if (RewritePages(other, RenameIn(other))) other.Save(path);
                }
                SheetLinks.PageRenamed(home, oldName, newName, vaultNotes?.Invoke() ?? Enumerable.Empty<string>());
            }

            calc.RecalcAll();
            changed?.Invoke(document);
        }

        // Rewrites every reference to cells of a page whose rows or columns moved, loaded, on disk or in notes; the page is already shifted.
        public static void Shifted(SheetDocument document, SheetPage shifted, bool column, int at, int count)
        {
            string? home = null;
            foreach ((string path, SheetDocument loaded) in documents)
                if (ReferenceEquals(loaded, document)) home = path;

            Func<SheetPage, string, string> ShiftIn(SheetDocument owner) => (formulaPage, raw) => SheetFormula.ShiftCells(raw, (file, page) =>
                file != null ? home != null && Names(file, home) && page!.Equals(shifted.name, StringComparison.OrdinalIgnoreCase)
                : page == null ? ReferenceEquals(formulaPage, shifted)
                : ReferenceEquals(owner, document) && page.Equals(shifted.name, StringComparison.OrdinalIgnoreCase),
                column, at, count);

            RewriteFormulas(document, ShiftIn(document));
            foreach ((string path, SheetDocument other) in documents)
            {
                if (ReferenceEquals(other, document) || other.isCsv || !RewriteFormulas(other, ShiftIn(other))) continue;
                if (TabViewControl.FindOpenDocument(path, out _) == null) other.Save(path);
                changed?.Invoke(other);
            }

            if (home != null)
            {
                foreach (string file in vaultSheets?.Invoke() ?? Enumerable.Empty<string>())
                {
                    string path = Path.GetFullPath(file);
                    if (documents.ContainsKey(path) || !File.Exists(path)) continue;
                    SheetDocument other = SheetDocument.Load(path);
                    if (RewriteFormulas(other, ShiftIn(other))) other.Save(path);
                }
                SheetLinks.Shifted(home, shifted.name, column, at, count, vaultNotes?.Invoke() ?? Enumerable.Empty<string>());
            }

            calc.RecalcAll();
            changed?.Invoke(document);
        }

        private static bool RewritePages(SheetDocument document, Func<string?, string, string?> rename) =>
            RewriteFormulas(document, raw => SheetFormula.RenamePage(raw, rename));

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

        // Rekeys the file and rewrites every reference to it, loaded or on disk; a CSV renamed to a CSV renames its page too.
        public static void Renamed(string oldPath, string newPath, IEnumerable<string> vaultSheets)
        {
            oldPath = Path.GetFullPath(oldPath);
            newPath = Path.GetFullPath(newPath);
            string oldName = BaseName(oldPath);
            string newName = BaseName(newPath);
            bool pageFollows = SheetCsv.IsCsv(oldPath) && SheetCsv.IsCsv(newPath);

            if (documents.Remove(oldPath, out SheetDocument? moved))
            {
                moved.name = newName;
                moved.isCsv = SheetCsv.IsCsv(newPath);
                if (pageFollows) moved.pages[0].name = newName;
                documents[newPath] = moved;
            }

            string NewPage(string page) => pageFollows && page.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : page;
            Func<string, string> RenameIn(SheetDocument owner) => raw => SheetFormula.RenamePrefix(raw, (file, page) =>
                file != null ? (Names(file, oldPath) ? (Renamed(file, oldPath, newPath), NewPage(page)) : null)
                : ReferenceEquals(owner, moved) ? (null, NewPage(page)) : null);

            foreach ((string path, SheetDocument document) in documents)
            {
                if (document.isCsv && !ReferenceEquals(document, moved) || !RewriteFormulas(document, RenameIn(document))) continue;
                if (TabViewControl.FindOpenDocument(path, out _) == null) document.Save(path);
                changed?.Invoke(document);
            }

            foreach (string file in vaultSheets)
            {
                string path = Path.GetFullPath(file);
                if (documents.ContainsKey(path) || !File.Exists(path)) continue;
                SheetDocument document = SheetDocument.Load(path);
                if (RewriteFormulas(document, RenameIn(document))) document.Save(path);
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

        private static bool RewriteFormulas(SheetDocument document, Func<string, string> rewrite) =>
            RewriteFormulas(document, (_, raw) => rewrite(raw));

        private static bool RewriteFormulas(SheetDocument document, Func<SheetPage, string, string> rewrite)
        {
            bool any = false;
            foreach (SheetPage page in document.pages)
                foreach (SheetLayer layer in page.layers)
                    foreach (SheetCell cell in layer.cells.Values)
                    {
                        if (!SheetFormula.IsFormula(cell.raw)) continue;
                        string rewritten = rewrite(page, cell.raw);
                        if (rewritten == cell.raw) continue;
                        cell.raw = rewritten;
                        any = true;
                    }
            return any;
        }

        // A file part names a path when it is the file's name or the end of its path, as vault links resolve.
        internal static bool Names(string file, string path)
        {
            string name = file.Trim().Replace('\\', '/');
            string target = path.Replace('\\', '/');
            if (SheetCsv.IsCsv(target) || SheetCsv.IsCsv(name))
                return SheetCsv.IsCsv(target) && target.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);

            if (name.EndsWith(SheetDocument.extension, StringComparison.OrdinalIgnoreCase))
                name = name[..^SheetDocument.extension.Length];
            return target[..^SheetDocument.extension.Length].EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);
        }

        // The file part naming newPath where it named oldPath, keeping any folder it gave and whether it wrote an extension.
        internal static string Renamed(string file, string oldPath, string newPath)
        {
            string trimmed = file.Trim();
            string extension = SheetCsv.IsCsv(trimmed) ? SheetCsv.extension
                : trimmed.EndsWith(SheetDocument.extension, StringComparison.OrdinalIgnoreCase) ? SheetDocument.extension
                : "";
            string folder = trimmed[..(trimmed.Length - extension.Length - BaseName(oldPath).Length)];
            string written = SheetCsv.IsCsv(newPath) ? SheetCsv.extension : extension.Length == 0 ? "" : SheetDocument.extension;
            return folder + BaseName(newPath) + written;
        }

        // A reference's file part for a path: a sheet's name without ".sheet.xml", a CSV's with ".csv".
        internal static string FileName(string path) =>
            SheetCsv.IsCsv(path) ? Path.GetFileName(path) : BaseName(path);

        internal static string BaseName(string path) =>
            Path.GetFileName(path)[..^(SheetCsv.IsCsv(path) ? SheetCsv.extension : SheetDocument.extension).Length];

        private static void RaiseAll() => changed?.Invoke(null);
        #endregion
    }
}
