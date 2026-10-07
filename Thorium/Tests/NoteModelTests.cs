using ArctisAurora.Core.Data;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Testing;
using ArctisAurora.Core.UI;
using System.Xml.Linq;

namespace Thorium.Tests
{
    internal static class NoteModelTests
    {
        [A_XSDActionDependency("NoteModel.SaveIsStable", "Test")]
        private static IEnumerator<int> SaveIsStable(TestContext t)
        {
            string notes = Path.GetDirectoryName(Path.GetFullPath(Paths.Doc("../../Notes/SampleNote.xml")))!;
            List<(string name, string extension, string text)> sources = Directory
                .EnumerateFiles(notes, "*", SearchOption.AllDirectories)
                .Where(path => RichTextDocument.extensions.Contains(Path.GetExtension(path).ToLowerInvariant())
                    && !SheetDocument.IsSheet(path) && !PlannerDocument.IsPlanner(path))
                .Select(path => (Path.GetRelativePath(notes, path), Path.GetExtension(path).ToLowerInvariant(), File.ReadAllText(path)))
                .ToList();
            t.Check(sources.Count > 0, "the vault's sample notes are found");

            sources.Add(("everything.xml", ".xml", Everything.ToString()));
            sources.Add(("plain.txt", ".txt", "first line\n\nthird line"));
            sources.Add(("source.tex", ".tex", "\\documentclass{article}\n\\begin{document}\nx\n\\end{document}\n"));

            foreach ((string name, string extension, string text) in sources)
            {
                string once = RoundTrip(text, extension, name);
                string twice = RoundTrip(once, extension, name);
                t.Check(once == twice, $"{name} saves the same after a reload");
            }

            yield break;
        }

        [A_XSDActionDependency("NoteModel.LoadHeadless", "Test")]
        private static IEnumerator<int> LoadHeadless(TestContext t)
        {
            DataPool controls = DataManager.Get("UIElements");
            int before = controls.Count;
            RichTextDocument document = DocumentXml.Parse(Everything);
            t.Check(controls.Count == before, $"parsing a note with blocks, tables and inserts builds no control ({controls.Count - before} rows)");
            t.Check(document.blocks.OfType<NoteTable>().Count() == 2 && document.blocks.OfType<NoteBlock>().Count() == 9,
                "the model holds the note's tables and blocks");
            yield break;
        }

        // A note's text read and written the way RichTextDocument does, without the file's timestamps.
        private static string RoundTrip(string text, string extension, string name)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            RichTextDocument document = DocumentXml.Parse(extension switch
            {
                ".md" => MarkdownFormat.Read(text, stem),
                ".txt" => PlainTextFormat.Read(text, stem),
                ".tex" => TexSourceFormat.Read(text, stem),
                _ => XElement.Parse(text)
            });
            XElement root = DocumentXml.ToXml(document);
            return extension switch
            {
                ".md" => MarkdownFormat.Write(root),
                ".txt" => PlainTextFormat.Write(root),
                ".tex" => TexSourceFormat.Write(root, "\n"),
                _ => root.ToString()
            };
        }

        private static XElement Block(string text, params object[] attributes) =>
            new XElement("Block", attributes, new XElement("Run", new XAttribute("Text", text)));

        private static XElement Cell(params object[] content) => new XElement("Cell", content);

        // Every block, run, table and insert feature the note format carries.
        private static readonly XElement Everything = new XElement("Document",
            new XAttribute("Name", "Everything"), new XAttribute("Palette", "thorium-light"), new XAttribute("ReadOnly", "true"),
            new XElement("DocumentLayout", new XAttribute("BlockSpacing", "8"),
                new XElement("Page", new XAttribute("Mode", "Paged"), new XAttribute("PageNumbers", "true"))),
            Block("Title", new XAttribute("StylingType", "Heading1")),
            new XElement("Block",
                new XAttribute("Align", "Center"), new XAttribute("Indent", "12"), new XAttribute("SpaceBefore", "6"),
                new XAttribute("PageBreak", "Page"), new XAttribute("PageStyle", "plain"),
                new XAttribute("MarkLeft", "L"), new XAttribute("MarkRight", "R"),
                new XElement("Run", new XAttribute("Text", "plain ")),
                new XElement("Run", new XAttribute("Text", "bold"), new XAttribute("Bold", "true"), new XAttribute("Italic", "true")),
                new XElement("Run", new XAttribute("Text", " red"), new XAttribute("ColorHex", "#C42B1E"), new XAttribute("Underline", "true"),
                    new XAttribute("Strikethrough", "true"), new XAttribute("HighlightHex", "#F6E7A8")),
                new XElement("Run", new XAttribute("Text", " big"), new XAttribute("FontSize", "24"), new XAttribute("FontSizeAuthored", "true"))),
            Block("item", new XAttribute("List", "Bullet"), new XAttribute("Level", "1"), new XAttribute("Marker", "LowerAlpha"), new XAttribute("Start", "3")),
            Block("done", new XAttribute("List", "Task"), new XAttribute("Checked", "true")),
            Block("int x = 1;", new XAttribute("StylingType", "Code"), new XAttribute("Language", "csharp"), new XAttribute("Wrap", "true")),
            new XElement("Block",
                new XElement("Run", new XAttribute("Text", "pic ")),
                new XElement("Run", new XAttribute("Image", "nosuch.png"), new XAttribute("Width", "40"), new XAttribute("Height", "30"),
                    new XAttribute("Wrap", "Square"), new XAttribute("X", "5"), new XAttribute("Y", "6"), new XAttribute("Rotation", "10")),
                new XElement("Run", new XAttribute("Text", " math ")),
                new XElement("Run", new XAttribute("Math", "x^2")),
                new XElement("Run", new XAttribute("Math", "y"), new XAttribute("Display", "true")),
                new XElement("Run", new XAttribute("Sheet", "Budget.sheet.xml#Sheet1!A1")),
                new XElement("Run", new XAttribute("Space", "12")),
                new XElement("Run", new XAttribute("Text", "fn"), new XAttribute("Note", "n1"))),
            new XElement("Table",
                new XAttribute("Borders", "false"), new XAttribute("Align", "Center"), new XAttribute("SpaceBefore", "4"),
                new XAttribute("PageBreak", "Clear"), new XAttribute("Padding", "3 2"),
                new XElement("Column", new XAttribute("Width", "100"), new XAttribute("RuleLeft", "Plain")),
                new XElement("Column", new XAttribute("Width", "80")),
                new XElement("Column", new XAttribute("Width", "60"), new XAttribute("RuleRight", "Double")),
                new XElement("Row",
                    Cell(new XAttribute("ColumnSpan", "2"), new XAttribute("RuleAbove", "Heavy"), new XAttribute("RuleBelow", "Light 0.5"),
                        new XAttribute("TrimBelow", "Left"), Block("span"), Block("second line")),
                    Cell(Block("c"))),
                new XElement("Row", Cell(), Cell(Block("b")), Cell(Block("c2")))),
            new XElement("Float", new XAttribute("Kind", "table"), new XAttribute("Placement", "h"),
                new XElement("Table", new XElement("Column", new XAttribute("Width", "50")), new XElement("Row", Cell(Block("in float")))),
                Block("caption")),
            new XElement("Footnote", new XAttribute("Id", "n1"), Block("note text")),
            Block("end"));
    }
}
