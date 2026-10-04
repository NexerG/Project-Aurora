using ArctisAurora.Core.Editing;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // A sheet file: pages of a grid, each page a stack of layers over the same cells.
    public class SheetDocument
    {
        public const string extension = ".sheet.xml";

        public string? name;
        public readonly List<SheetPage> pages = new List<SheetPage>();

        // elements this version does not read, written back as they came
        public readonly List<XElement> extra = new List<XElement>();

        // one history per file, whichever tab edits it
        public readonly UndoStack undo = new UndoStack();

        public static bool IsSheet(string path) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

        public static SheetDocument Load(string path) => SheetXml.Load(path);

        public void Save(string path) => SheetXml.Save(this, path);

        // One page holding one layer.
        public static SheetDocument Blank(string? name)
        {
            SheetDocument document = new SheetDocument { name = name };
            document.pages.Add(SheetPage.Blank("Sheet 1"));
            return document;
        }

        #region ---- addressing ----
        public static long Key(int row, int column) => ((long)row << 32) | (uint)column;

        public static int RowOf(long key) => (int)(key >> 32);

        public static int ColumnOf(long key) => (int)(key & 0xFFFFFFFFL);

        // 0 → A, 25 → Z, 26 → AA.
        public static string ColumnName(int column)
        {
            StringBuilder name = new StringBuilder();
            for (int c = column + 1; c > 0; c = (c - 1) / 26)
                name.Insert(0, (char)('A' + (c - 1) % 26));
            return name.ToString();
        }

        public static string Address(int row, int column) => ColumnName(column) + (row + 1).ToString(CultureInfo.InvariantCulture);

        // "B3" → row 2, column 1; a bare "B" or "3" reads only its own half.
        public static bool TryParseAddress(string text, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (string.IsNullOrEmpty(text)) return false;

            int i = 0;
            int c = 0;
            for (; i < text.Length && char.IsAsciiLetter(text[i]); i++)
                c = c * 26 + (char.ToUpperInvariant(text[i]) - 'A' + 1);
            if (i > 0) column = c - 1;

            if (i < text.Length)
            {
                if (!int.TryParse(text.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out int r) || r < 1) return false;
                row = r - 1;
            }

            return row >= 0 || column >= 0;
        }
        #endregion
    }

    // One tab of a sheet file: band sizes shared by its layers.
    public class SheetPage
    {
        public const float defaultColumnWidth = 100f;
        public const float defaultRowHeight = 24f;

        public string name = "";
        public readonly Dictionary<int, float> columnWidths = new Dictionary<int, float>();
        public readonly Dictionary<int, float> rowHeights = new Dictionary<int, float>();
        public readonly List<SheetLayer> layers = new List<SheetLayer>();
        public readonly List<XElement> extra = new List<XElement>();

        public static SheetPage Blank(string name)
        {
            SheetPage page = new SheetPage { name = name };
            page.layers.Add(new SheetLayer { name = "Layer 1" });
            return page;
        }

        #region ---- bands ----
        public float ColumnWidth(int column) => columnWidths.TryGetValue(column, out float w) ? w : defaultColumnWidth;

        public float RowHeight(int row) => rowHeights.TryGetValue(row, out float h) ? h : defaultRowHeight;

        public float ColumnLeft(int column) => Start(column, defaultColumnWidth, columnWidths);

        public float RowTop(int row) => Start(row, defaultRowHeight, rowHeights);

        public int ColumnAt(float x) => IndexAt(x, defaultColumnWidth, columnWidths);

        public int RowAt(float y) => IndexAt(y, defaultRowHeight, rowHeights);

        private static float Start(int index, float size, Dictionary<int, float> custom)
        {
            float start = index * size;
            foreach (KeyValuePair<int, float> band in custom)
                if (band.Key < index) start += band.Value - size;
            return start;
        }

        // The band holding a position: the last one starting at or before it.
        private static int IndexAt(float position, float size, Dictionary<int, float> custom)
        {
            if (position <= 0f) return 0;

            float smallest = size;
            foreach (float value in custom.Values)
                if (value > 0f) smallest = MathF.Min(smallest, value);

            int lo = 0;
            int hi = (int)MathF.Min(int.MaxValue / 2, position / smallest) + 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (Start(mid, size, custom) <= position) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }
        #endregion

        // The text a cell shows: the topmost visible layer holding it.
        public string? Shown(int row, int column)
        {
            long key = SheetDocument.Key(row, column);
            for (int i = layers.Count - 1; i >= 0; i--)
                if (layers[i].visible && layers[i].cells.TryGetValue(key, out SheetCell? cell))
                    return cell.raw;
            return null;
        }

        // One past the last row and column any layer uses.
        public (int rows, int columns) Used()
        {
            int rows = 0;
            int columns = 0;
            foreach (SheetLayer layer in layers)
                foreach (long key in layer.cells.Keys)
                {
                    rows = Math.Max(rows, SheetDocument.RowOf(key) + 1);
                    columns = Math.Max(columns, SheetDocument.ColumnOf(key) + 1);
                }
            return (rows, columns);
        }
    }

    // Cells over a page's grid; an empty cell is absent.
    public class SheetLayer
    {
        public string name = "";
        public bool visible = true;
        public readonly Dictionary<long, SheetCell> cells = new Dictionary<long, SheetCell>();
        public readonly List<XElement> extra = new List<XElement>();

        public string? Get(int row, int column) =>
            cells.TryGetValue(SheetDocument.Key(row, column), out SheetCell? cell) ? cell.raw : null;

        public void Set(int row, int column, string? raw)
        {
            long key = SheetDocument.Key(row, column);
            if (string.IsNullOrEmpty(raw)) cells.Remove(key);
            else cells[key] = new SheetCell { raw = raw };
        }
    }

    public sealed class SheetCell
    {
        public string raw = "";
    }
}
