using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Tex;
using ArctisAurora.Core.Threading;
using ArctisAurora.EngineWork;
using System.Numerics;
using System.Xml.Linq;

namespace ArctisAurora.Core.UI
{
    // One open .tex file: the source, a grip, and the typeset preview over its error list.
    public class TexEditorControl : StackPanelControl, IFileEditor
    {
        public const double recompileDelay = 0.5;
        private const float rowHeight = 22f;
        private const int maxErrorRows = 5;
        private const int fontSize = 13;

        public readonly DocumentEditorControl source;
        public readonly DocumentEditorControl preview;
        public readonly ScrollableControl errors;
        private readonly StackPanelControl errorRows = new StackPanelControl { alpha = 0f, horizontalAlignment = HorizontalAlignment.Stretch };

        // each preview block's source line, 0 = none
        private readonly List<int> blockLines = new List<int>();

        // the last edit not yet compiled
        private double editedAt;

        public string? path => source.path;

        bool IFileEditor.isDirty => source.session?.isDirty == true;

        public TexEditorControl()
        {
            orientation = Orientation.Horizontal;
            horizontalAlignment = HorizontalAlignment.Stretch;
            verticalAlignment = VerticalAlignment.Stretch;
            alpha = 0f;

            source = new DocumentEditorControl { contextMenu = "note", widthStar = 1f, verticalAlignment = VerticalAlignment.Stretch };
            source.onEdited = Edited;

            preview = new DocumentEditorControl { heightStar = 1f, horizontalAlignment = HorizontalAlignment.Stretch };
            preview.onSave = Save;

            errors = new ScrollableControl { alpha = 0f, horizontalAlignment = HorizontalAlignment.Stretch };
            errors.AddChild(errorRows);
            errors.Hide();

            StackPanelControl column = new StackPanelControl { alpha = 0f, widthStar = 1f, verticalAlignment = VerticalAlignment.Stretch };
            column.AddChild(preview);
            column.AddChild(errors);

            SplitterControl grip = new SplitterControl { preferredWidth = 5f };
            AddChild(source);
            AddChild(grip);
            AddChild(column);
        }

        public void LoadPath(string nameOrPath)
        {
            source.LoadPath(nameOrPath);
            Recompile();
        }

        #region ---- file ----
        public void Save() => source.Save();

        public void Repath(string newPath, string name) => source.Repath(newPath, name);

        public SessionTab ViewState() => source.ViewState();

        public void RestoreView(SessionTab view) => source.RestoreView(view);
        #endregion

        #region ---- compile ----
        private void Edited()
        {
            editedAt = Engine.totalTime;
            SetTicking(true);
            FrameScheduler.RequestFrameAt(editedAt + recompileDelay);
        }

        public override void OnTick()
        {
            base.OnTick();
            if (Engine.totalTime < editedAt + recompileDelay)
            {
                FrameScheduler.RequestFrameAt(editedAt + recompileDelay);
                return;
            }

            SetTicking(false);
            Recompile();
        }

        // Typesets the source into the preview and lists what went wrong.
        public void Recompile()
        {
            if (source.activeDocument == null) return;

            Profiling.Zone.Start("Tex.Compile");
            string text = TexSourceFormat.Write(DocumentXml.ToXml(source.activeDocument), "\n");
            blockLines.Clear();
            XElement tree = TexLowering.Compile(text, out List<TexError> found, blockLines);
            RichTextDocument document = DocumentXml.Parse(tree);
            document.readOnly = true;
            Vector2 scroll = preview.GetScrollOffset();
            preview.LoadDocument(document);
            preview.SetScrollOffset(scroll);
            Profiling.Zone.End("Tex.Compile");

            ShowErrors(found);
        }

        private void ShowErrors(List<TexError> found)
        {
            for (int i = errorRows.children.Count - 1; i >= 0; i--)
                errorRows.children[i].Destroy();

            if (found.Count == 0)
            {
                errors.Hide();
                return;
            }

            foreach (TexError error in found)
                errorRows.AddChild(ErrorRow(error));
            errors.preferredHeight = Math.Min(found.Count, maxErrorRows) * rowHeight;
            errors.Show();
        }

        private ButtonControl ErrorRow(TexError error)
        {
            ButtonControl row = new ButtonControl
            {
                preferredHeight = rowHeight,
                horizontalAlignment = HorizontalAlignment.Stretch,
                padding = new Thickness(6f, 0f)
            };
            row.PaintOr(null, PaletteRole.Clear);
            row.AddChild(new LabelControl
            {
                text = error.line > 0 ? $"{error.line}:{error.column}  {error.message}" : error.message,
                fontSize = fontSize,
                role = PaletteRole.MutedInk,
                hitTestable = false,
                horizontalPosition = 0f,
                verticalPosition = 0.5f
            });

            if (error.line > 0)
                row.RegisterOnPress(e =>
                {
                    if (e.button != PointerEvent.leftButton) return false;
                    source.GoTo(error.line - 1, Math.Max(0, error.column - 1));
                    source.FocusCaret();
                    return true;
                });
            return row;
        }
        #endregion

        #region ---- click to source ----
        // A double-click in the preview moves the source caret to that block's line.
        public override bool OnPointerRelease(PointerEvent e)
        {
            if (e.tapCount != 2 || e.button != PointerEvent.leftButton || !InPreview(e.target)) return false;

            int index = preview.CaretBlock != null ? preview.activeDocument.blocks.IndexOf(preview.CaretBlock) : -1;
            if (index >= 0 && index < blockLines.Count && blockLines[index] > 0) source.GoTo(blockLines[index] - 1, 0);
            return false;
        }

        private bool InPreview(Control? control)
        {
            for (; control != null; control = control.parent as Control)
                if (ReferenceEquals(control, preview)) return true;
            return false;
        }
        #endregion
    }
}
