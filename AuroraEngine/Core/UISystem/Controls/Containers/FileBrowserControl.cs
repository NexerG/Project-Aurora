using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls.Text;
using ArctisAurora.EngineWork.Registry;

namespace ArctisAurora.Core.UISystem.Controls.Containers
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

        // row palette
        [A_XSDElementProperty("RowColorHex", "UI", "Ground of a row at rest.")]
        public string rowColorHex = "#171717";

        [A_XSDElementProperty("RowHoverColorHex", "UI", "Ground of a hovered row.")]
        public string rowHoverColorHex = "#232323";

        [A_XSDElementProperty("RowPressColorHex", "UI", "Ground of a held row.")]
        public string rowPressColorHex = "#2D2D2D";

        [A_XSDElementProperty("FolderColorHex", "UI", "Text color of a folder row.")]
        public string folderColorHex = "#8A8A8A";

        [A_XSDElementProperty("FileColorHex", "UI", "Text color of a file row.")]
        public string fileColorHex = "#D4D4D4";

        [A_XSDElementProperty("RowFieldColorHex", "UI", "Ground of a row's name while it is being renamed.")]
        public string rowFieldColorHex = "#2D2D2D";
        #endregion

        private readonly StackPanelControl rows = new StackPanelControl();

        // Vault, project folder or whatever else the derivative lists.
        protected FileObject? root;

        protected abstract string RootPath { get; }

        protected abstract void PopulateRows();

        protected abstract void Activate(FileObject file);

        // Files a row is built for. Folders are the derivative's business.
        protected virtual bool Accepts(FileObject file) => true;

        protected virtual string DisplayName(FileObject file) => file.name;

        // Entries one row offers on right click. Add-only, the same contract as BuildContextMenu.
        protected internal virtual void BuildRowMenu(FileObject file, ContextMenuBuilder menu) { }

        // What a committed rename does. Nothing by default — a browser that cannot rename says so by
        // not offering the entry.
        protected virtual void Rename(FileObject file, string newName) { }

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

            // The viewport paints the browser's ground; the panel inside it must not, or the
            // default mask covers the whole column.
            rows.maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible");
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
        protected void AddRow(FileObject file, int depth, string expander, Action activate)
        {
            LabelControl gutter = new LabelControl
            {
                text = expander,
                fontSize = rowFontSize,
                preferredWidth = gutterWidth,
                controlColorHex = folderColorHex
            };

            EditableLabelControl name = new EditableLabelControl
            {
                text = DisplayName(file),
                fontSize = rowFontSize,
                textColorHex = file.type == FileObject.FileType.Directory ? folderColorHex : fileColorHex,
                fieldColorHex = rowFieldColorHex
            };

            // Sits between the button and its text, so it has to pass everything through — a
            // StackPanel bubbles nothing by default and would eat the row's hover and release.
            StackPanelControl content = new StackPanelControl
            {
                orientation = StackPanelControl.Orientation.Horizontal,
                maskAsset = AssetRegistries.GetAsset<TextureAsset>("invisible"),
                horizontalPosition = 0f
            };
            content.BubbleAll();
            content.AddChild(gutter);
            content.AddChild(name);

            FileRowControl row = new FileRowControl
            {
                file = file,
                browser = this,
                label = name,
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                margin = new Thickness(0, 0, 0, depth * indent),
                padding = new Thickness(0, 0, 0, rowInset),
                controlColorHex = rowColorHex,
                hoverColorHex = rowHoverColorHex,
                pressColorHex = rowPressColorHex
            };
            row.AddChild(content);
            row.RegisterOnRelease(activate);

            rows.AddChild(row);
        }
    }
}
