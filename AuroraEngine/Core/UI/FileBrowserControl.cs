using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    // A scrolling list of rows over a FileObject tree. The base owns the root and what a row looks
    // like; a derivative decides which entries become rows and what activating one does.
    public abstract class FileBrowserControl : ScrollableControl
    {
        #region properties
        // row metrics
        [A_XSDElementProperty("RowHeight", "UI", "Height of a single row in pixels.")]
        public int rowHeight = 22;

        [A_XSDElementProperty("Indent", "UI", "Left offset a row gains per level of depth, in pixels.")]
        public float indent = 12f;

        [A_XSDElementProperty("RowSpacing", "UI", "Space between rows in pixels.")]
        public float rowSpacing = 2f;

        [A_XSDElementProperty("RowInset", "UI", "Space between a row's left edge and its text, in pixels.")]
        public float rowInset = 6f;

        [A_XSDElementProperty("GutterWidth", "UI", "Width of the expander column ahead of a row's name, in pixels.")]
        public int gutterWidth = 12;

        [A_XSDElementProperty("RowFontSize", "UI", "Font size of a row's text in pixels.")]
        public int rowFontSize = 14;

        // row palette — a derivative populates in its constructor, so these repaint what is already there
        [A_XSDElementProperty("RowColorHex", "UI", "Ground of a row at rest.")]
        public string? rowColorHex { get => field; set { field = value; RestyleRows(); } }

        [A_XSDElementProperty("RowHoverColorHex", "UI", "Ground of a hovered row.")]
        public string? rowHoverColorHex { get => field; set { field = value; RestyleRows(); } }

        [A_XSDElementProperty("RowPressColorHex", "UI", "Ground of a held row.")]
        public string? rowPressColorHex { get => field; set { field = value; RestyleRows(); } }

        [A_XSDElementProperty("FolderColorHex", "UI", "Text color of a folder row.")]
        public string? folderColorHex { get => field; set { field = value; RestyleRows(); } }

        [A_XSDElementProperty("FileColorHex", "UI", "Text color of a file row.")]
        public string? fileColorHex { get => field; set { field = value; RestyleRows(); } }

        [A_XSDElementProperty("RowFieldColorHex", "UI", "Ground of a row's name while it is being renamed.")]
        public string? rowFieldColorHex { get => field; set { field = value; RestyleRows(); } }
        #endregion

        private readonly StackPanelControl rows = new StackPanelControl();

        // Vault, project folder or whatever else the derivative lists.
        protected FileObject? root;

        // the highlighted entry
        private string? currentPath;

        protected abstract string RootPath { get; }

        protected abstract void PopulateRows();

        protected abstract void Activate(FileObject file);

        // Files a row is built for. Folders are the derivative's business.
        protected virtual bool Accepts(FileObject file) => true;

        protected virtual string DisplayName(FileObject file) => file.name;

        // What a committed rename does. Nothing by default — a browser that cannot rename says so by
        // not offering the entry.
        protected virtual void Rename(FileObject file, string newName) { }

        // The menu a row offers. Null leaves the row with whatever the browser itself names, which is
        // what a folder and a file having the same entries looks like.
        protected virtual string? RowContextMenu(FileObject file) => null;

        // Turns one entry's name into a field, in place.
        protected void BeginRename(FileObject file)
        {
            foreach (Entity child in rows.children)
                if (child is FileRowControl row && ReferenceEquals(row.file, file))
                {
                    row.label.BeginEdit(name => Rename(file, name));
                    return;
                }
        }

        public FileBrowserControl()
        {
            scrollDirection = ScrollDirection.Vertical;

            // The viewport paints the browser's ground; the panel inside it must not, or its own
            // quad covers the whole column.
            rows.alpha = 0f;
            rows.orientation = StackPanelControl.Orientation.Vertical;
            AddChild(rows);
        }

        // Re-reads the root folder and replaces every row.
        public void Rebuild()
        {
            foreach (Entity row in rows.children.ToArray())
                row.Destroy();

            rows.Spacing = rowSpacing;

            string path = RootPath;
            root = Directory.Exists(path) ? new FileObject(path) : null;
            if (root == null) return;

            PopulateRows();
        }

        // A row is a button over an expander gutter and the entry's name. The gutter is kept on a
        // file row so its name lines up with the folder names around it.
        protected FileRowControl AddRow(FileObject file, int depth, string expander, Action activate)
        {
            LabelControl gutter = new LabelControl
            {
                text = expander,
                fontSize = rowFontSize,
                preferredWidth = gutterWidth,
                horizontalPosition = 0f
            };
            gutter.PaintOr(folderColorHex, PaletteRole.MutedInk);

            EditableLabelControl name = new EditableLabelControl
            {
                text = DisplayName(file),
                fontSize = rowFontSize,
                fieldColorHex = rowFieldColorHex
            };
            PaintName(name, file);

            StackPanelControl content = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                alpha = 0f,
                horizontalPosition = 0f
            };
            content.AddChild(gutter);
            content.AddChild(name);

            // A row with a menu of its own is the whole menu — the browser's ground entries are for
            // the ground, and the walk would otherwise append them under every row.
            string rowMenu = RowContextMenu(file);

            FileRowControl row = new FileRowControl
            {
                file = file,
                browser = this,
                label = name,
                gutter = gutter,
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                clipOutOfBounds = true,
                margin = new Thickness(0, 0, 0, depth * indent),
                padding = new Thickness(0, 0, 0, rowInset),
                cornerRole = CornerRole.Row,
                hoverColorHex = rowHoverColorHex,
                pressColorHex = rowPressColorHex,
                contextMenu = rowMenu,
                stopsContextMenu = rowMenu != null
            };
            PaintRow(row);
            row.AddChild(content);
            row.RegisterOnRelease(_ => { activate(); return true; });

            rows.AddChild(row);
            return row;
        }

        // Repaints rows the constructor already built, because the host's attributes arrive after it.
        private void RestyleRows()
        {
            if (rows == null) return;

            foreach (Entity child in rows.children)
            {
                if (child is not FileRowControl row) continue;

                PaintRow(row);
                row.hoverColorHex = rowHoverColorHex;
                row.pressColorHex = rowPressColorHex;

                PaintName(row.label, row.file);
                row.label.fieldColorHex = rowFieldColorHex;

                if (row.children.Count > 0 && row.children[0] is Control content
                    && content.children.Count > 0 && content.children[0] is LabelControl gutter)
                    gutter.PaintOr(folderColorHex, PaletteRole.MutedInk);
            }
        }

        // Highlights the row for path, or none.
        public void SetCurrent(string? path)
        {
            if (string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase)) return;
            currentPath = path;

            foreach (Entity child in rows.children)
                if (child is FileRowControl row) PaintRow(row);
        }

        private void PaintRow(FileRowControl row)
        {
            bool current = currentPath != null && string.Equals(row.file.path, currentPath, StringComparison.OrdinalIgnoreCase);
            row.PaintOr(current ? null : rowColorHex, current ? PaletteRole.SubField : PaletteRole.Clear);
            row.accentRole = current ? AccentRole.Row : AccentRole.None;
        }

        private void PaintName(EditableLabelControl name, FileObject file)
        {
            if (file.type == FileObject.FileType.Directory) name.PaintText(folderColorHex, PaletteRole.MutedInk);
            else name.PaintText(fileColorHex, PaletteRole.Ink);
        }
    }
}
