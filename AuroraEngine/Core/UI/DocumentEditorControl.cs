using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Editing;
using ArctisAurora.Core.Filing;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace ArctisAurora.Core.UI
{
    // One open note: the scroll viewport, the document under it, and the session behind that.
    [A_XSDType("DocumentEditor", "UI")]
    public class DocumentEditorControl : ScrollableControl, IContext, IClipboardTarget, IFileEditor
    {
        public RichTextDocument activeDocument { get; private set; } = null!;
        public DocumentEditSession session { get; private set; }

        public string? path => session?.path;
        bool IFileEditor.isDirty => session?.isDirty == true;

        [A_XSDElementProperty("CaretColorHex", "UI", "Color of the insertion caret.")]
        public string? caretColorHex
        {
            get => field;
            set { field = value; if (content != null) content.caretColorHex = value; }
        }

        [A_XSDElementProperty("SelectionColorHex", "UI", "Ground of the selection highlight behind the text.")]
        public string? selectionColorHex
        {
            get => field;
            set { field = value; if (content != null) content.selectionColorHex = value; }
        }

        private static readonly LogChannel Log = LogChannel.For("UI");

        private const float autoScrollRate = 0.25f;

        private DocumentControl content;
        private NotePropertiesControl? properties;

        // honoured at the end of Arrange, once every block has this frame's lines
        private bool scrollToCaretPending;
        private SessionTab? pendingView;

        public DocumentEditorControl()
        {
            scrollDirection = ScrollDirection.Both;
            overscroll = 0.5f;
            alpha = 0f;
            SheetBook.changed += BookChanged;
        }

        public override void OnDestroy()
        {
            SheetBook.changed -= BookChanged;
            if (activeDocument != null) activeDocument.changed -= NoteChanged;
            base.OnDestroy();
        }

        private void BookChanged(SheetDocument? edited) => content?.RefreshSheetLinks();

        private void NoteChanged(NoteChange change)
        {
            if (change.kind == NoteChangeKind.Palette) ApplyPalette();
        }

        [A_XSDElementProperty("Source", "UI", "Engine-XML note file to load into the editor.")]
        public string source
        {
            get => field;
            set
            {
                field = value;
                if (!string.IsNullOrEmpty(value))
                    LoadPath(value);
            }
        }

        // Normalized, because the session's path is what identifies an open note: a Source authored
        // relative to the documents folder resolves through Path.Combine, which leaves the ".."
        // segments in, and would not string-match the same file reached from a folder listing.
        public void LoadPath(string nameOrPath)
        {
            string path = Path.GetFullPath(Path.IsPathRooted(nameOrPath) ? nameOrPath : Paths.Doc(nameOrPath));
            RichTextDocument document = RichTextDocument.Load(path);

            session = new DocumentEditSession(document, path);
            LoadDocument(document);
        }

        public void LoadDocument(RichTextDocument document)
        {
            if (activeDocument != null) activeDocument.changed -= NoteChanged;
            activeDocument = document;
            document.changed += NoteChanged;

            // Only the content: the scroll thumbs are children too, and the fields holding them are
            // never rebuilt.
            content?.Destroy();

            if (Extension == ".tex") document.layout.page = new PageLayout { mode = PageMode.Pageless };

            content = new DocumentControl
            {
                blockSpacing = document.layout.blockSpacing,
                page = document.layout.Page,
                zoom = DocumentZoom,
                document = document,
                caretColorHex = caretColorHex,
                selectionColorHex = selectionColorHex,
                undo = session?.undo,
                alpha = 0f
            };
            AddChild(content);

            string extension = Extension;
            content.plainText = extension is ".txt" or ".tex";
            content.readOnly = document.readOnly;
            properties = extension is ".md" or ".xml" ? new NotePropertiesControl(this, extension == ".md") : null;
            if (properties != null)
            {
                ExpanderControl expander = new ExpanderControl();
                expander.AddChild(properties);
                content.header = expander;
            }

            foreach (NoteNode entry in document.blocks)
            {
                if (entry is NoteTable model)
                {
                    TableControl table = new TableControl(model);
                    table.ApplyLayout(document.layout);
                    content.AddChild(table.Hosted());
                    continue;
                }

                BlockControl block = new BlockControl((NoteBlock)entry);
                block.StartEffects();
                block.ApplyLayout(document.layout);
                content.AddChild(block);
            }

            ApplyPalette();
        }

        public void Save()
        {
            session?.Save();
            properties?.Refresh();
        }

        public void Repath(string newPath, string name)
        {
            session.Repath(newPath);
            session.document.name = name;
        }

        // Naming an unnamed note is a rename — the host follows the name onto the file, onto every
        // tab holding the note and onto its own list. Null when nothing owns the note that way.
        public Action<string>? onNamed;

        // What a save does in an editor with no session.
        public Action? onSave;

        // Writes the note, asking for a name first when it has never been named. onSaved runs once it
        // is on disk, onDiscarded if the note was left unwritten on purpose, onCancelled if the
        // answer was abandoned. Passing no onDiscarded leaves the prompt without that button.
        public void SaveNamed(Action onSaved = null, Action onDiscarded = null, Action onCancelled = null)
        {
            if (session == null) { onSave?.Invoke(); onSaved?.Invoke(); return; }

            if (!needsNaming)
            {
                Save();
                onSaved?.Invoke();
                return;
            }

            // An editor outside a tree has no window to prompt over. Answering for it beats asking
            // a prompt that refuses and leaves the caller waiting on a callback that never comes.
            RenderWindow window = UIEngine.WindowOf(this);
            if (window == null)
            {
                (onDiscarded ?? onSaved)?.Invoke();
                return;
            }

            NoteNameWindow.Ask(window, Path.GetFileNameWithoutExtension(session.path),
                name =>
                {
                    session.document.name = name;
                    Save();
                    onNamed?.Invoke(name);
                    onSaved?.Invoke();
                },
                onDiscarded,
                onCancelled);
        }

        #region ---- history ----
        // Every path that changes the document ends here, so the close paths can tell an edited note
        // from one that was only opened.
        public void MarkDirty()
        {
            session?.MarkDirty();
            onEdited?.Invoke();
        }

        public Action? onEdited;

        // One user action's worth of edits. A note with no session has no history, and the default
        // scope discards what is pushed into it.
        public EditScope BeginStep(string label, bool join = false) => session != null ? session.undo.Begin(label, join) : default;

        // a loaded note that is not marked ReadOnly
        private bool Writable => content != null && activeDocument?.readOnly != true;

        public void Undo()
        {
            if (session == null || !Writable || !session.undo.Undo()) return;

            content?.DisarmStyle();
            MarkDirty();
            RequestScrollToCaret();
        }

        public void Redo()
        {
            if (session == null || !Writable || !session.undo.Redo()) return;

            content?.DisarmStyle();
            MarkDirty();
            RequestScrollToCaret();
        }
        #endregion

        #region ---- styling ----
        // What a toggle reads its current state from, and what the format bar reflects.
        public CaretStyle? StyleSource => content?.StyleSource;

        public bool? SelectionAll(Func<StyleSpan, bool> test) => content?.SelectionAll(test);

        public TextStyleType CaretBlockStyling => content?.CaretBlockStyling ?? TextStyleType.Text;

        public void ApplyStyle(StyleDelta delta)
        {
            if (!Writable) return;

            using (BeginStep("Formatting"))
                if (content.ApplyStyle(delta)) MarkDirty();
        }

        // Nothing is written, so there is no step and no dirty note until the next character.
        public void ArmStyle(StyleDelta delta) => content?.ArmStyle(delta);

        public void DisarmStyle() => content?.DisarmStyle();

        // For a control that must take the active context before it can be used: it captures the
        // range on the way in and hands it back here, rather than asking what is selected once the
        // note no longer holds the caret.
        public bool SelectedRange(out DocumentAddress from, out DocumentAddress to)
        {
            from = to = default;
            return content != null && content.OrderedSelection(out from, out to);
        }

        public void ApplyStyleTo(DocumentAddress from, DocumentAddress to, StyleDelta delta)
        {
            if (!Writable) return;

            using (BeginStep("Formatting"))
                if (content.ApplyStyleTo(from, to, delta)) MarkDirty();
        }

        public void SetBlockStyling(TextStyleType type)
        {
            if (!Writable) return;

            using (BeginStep("Paragraph style"))
                if (content.SetBlockStyling(type)) MarkDirty();
        }

        public TextAlignment CaretBlockAlignment => content?.CaretBlockAlignment ?? TextAlignment.Left;

        // Markdown and plain text have no way to write it, so only .xml notes align.
        public bool CanAlign => content != null && !(Extension is ".md" or ".txt" or ".tex");

        public void SetAlignment(TextAlignment alignment)
        {
            if (!CanAlign || !Writable) return;

            using (BeginStep("Alignment"))
                if (content.SetBlockAlignment(alignment)) MarkDirty();
        }

        public void InsertRule()
        {
            if (!Writable) return;

            using (BeginStep("Horizontal line"))
                if (content.InsertRule()) MarkDirty();
            RequestScrollToCaret();
        }

        private string Extension => Path.GetExtension(session?.path ?? string.Empty).ToLowerInvariant();

        public void SetChecked(BlockControl block, bool value)
        {
            int index = content?.Blocks().IndexOf(block) ?? -1;
            if (index < 0 || !Writable) return;

            using (BeginStep("Check"))
                content.SetBlockList(index, b => b.isChecked = value);
            MarkDirty();
        }

        // Positive nests, negative un-nests.
        public void ShiftListLevel(int delta)
        {
            if (content == null) return;

            if (content.caretBlock?.parent?.parent is TableControl table
                && !(content.AtListItemStart && (delta > 0 || content.caretBlock.listLevel > 0)))
            {
                BlockControl? next = table.StepCell(content.caretBlock, delta);
                if (next != null) content.SetCaret(next, 0);
                else if (delta > 0 && Writable)
                    using (BeginStep("Insert row"))
                        if (content.InsertTableRow(true, true)) MarkDirty();
                RequestScrollToCaret();
                return;
            }
            if (!Writable) return;

            using (BeginStep(delta > 0 ? "Indent" : "Outdent"))
                if (content.ShiftCodeIndent(delta) || content.ShiftListLevel(delta)) MarkDirty();
        }

        public void SetListMarker(ListMarker marker)
        {
            if (!Writable) return;

            using (BeginStep("List marker"))
                if (content.SetListMarker(marker)) MarkDirty();
        }

        public void ContinueNumbering()
        {
            if (!Writable) return;

            using (BeginStep("Continue numbering"))
                if (content.ContinueNumbering()) MarkDirty();
        }

        public void SetPictureWrap(PictureWrap wrap)
        {
            if (!Writable) return;

            using (BeginStep("Wrap picture"))
                if (content.SetPictureWrap(wrap)) MarkDirty();
        }

        public void SetPictureCollision(PictureCollision collision)
        {
            if (!Writable) return;

            using (BeginStep("Picture collision"))
                if (content.SetPictureCollision(collision)) MarkDirty();
        }

        // Places an empty formula at the caret and opens its source.
        public void InsertFormula(bool display)
        {
            if (!Writable || content == null) return;

            DocumentAddress? at = content.PlaceMath(display);
            if (at == null)
            {
                Log.Info($"a formula cannot go in a plain text note, a code block or a rule; insert refused.");
                return;
            }
            FormulaPopup.Open(this, content, at.Value, true);
        }

        // A 3x3 table after the caret's block, its columns splitting the text width evenly.
        public void InsertTable()
        {
            if (!Writable) return;
            if (Extension is ".md" or ".txt" or ".tex")
            {
                Log.Info($"a {Extension} note cannot store a table; insert refused.");
                return;
            }

            PageLayout page = Page!;
            float text = page.SizePx().X - (page.marginLeft + page.marginRight) * PageLayout.PxPerMm;
            using (BeginStep("Insert table"))
            {
                if (content.InsertTable(3, 3, MathF.Floor(text / 3f))) MarkDirty();
                else Log.Info($"a table cannot go inside another table; insert refused.");
            }
            RequestScrollToCaret();
        }

        // A row or column command on the caret's table; nothing outside one.
        public void ChangeTable(string label, Func<DocumentControl, bool> change)
        {
            if (!Writable) return;

            using (BeginStep(label))
                if (change(content)) MarkDirty();
            RequestScrollToCaret();
        }

        // Opens the selected formula's source; false when no formula is selected.
        public bool EditFormula()
        {
            if (!Writable || content == null || !content.SelectedMath(out BlockControl block, out int index)) return false;

            FormulaPopup.Open(this, content, content.AddressOf(block, index), false);
            return true;
        }

        // The note's page format.
        public PageLayout? Page => activeDocument?.layout.Page;

        public int? PageAt(int block, int offset) => content?.PageAt(content.ViewOf(activeDocument.blocks[block]), offset);

        public void SetPage(PageLayout page)
        {
            if (content == null) return;

            using (BeginStep("Page"))
            {
                session?.undo.Push(new PageEdit(activeDocument, activeDocument.layout.page, page));
                activeDocument.SetPage(page);
            }
            MarkDirty();
            RequestScrollToCaret();
        }

        // The note's own palette, or the app's when it names none. Not undoable.
        public void SetPalette(string? name)
        {
            if (activeDocument == null) return;

            activeDocument.SetPalette(name);
            MarkDirty();
        }

        // Points the editor at the note's palette and paints that palette's ground.
        private void ApplyPalette()
        {
            string? name = activeDocument.palette;
            bool known = name != null && Palettes.Names.Contains(name);
            if (name != null && !known) Log.Warn($"Note palette '{name}' is not defined; showing the app's.");

            paletteName = known ? name! : "";
            role = known ? PaletteRole.Ground : PaletteRole.Clear;
            alpha = known ? 1f : 0f;
        }

        // The note's layout values. Not undoable.
        public void SetLayout(DocumentLayout layout)
        {
            if (content == null) return;

            activeDocument.SetLayout(layout);
            MarkDirty();
        }

        // One frontmatter key of a Markdown note, removed when value is null. Not undoable.
        public void SetFrontmatterValue(string key, string? value)
        {
            if (activeDocument == null) return;

            activeDocument.SetFrontmatterValue(key, value);
            MarkDirty();
        }

        // Whether the note's text refuses changes. Not undoable.
        public void SetReadOnly(bool value)
        {
            if (content == null) return;

            activeDocument.SetReadOnly(value);
            MarkDirty();
        }

        // The document zoom setting, clamped; 1 is 100%.
        private static float DocumentZoom =>
            Math.Clamp(SettingsRegistry.Get<UISettings>().documentZoom.percent, 25f, 400f) / 100f;

        [A_XSDActionDependency("Document.Rezoom", "Settings", "Re-lays every open note at the document zoom")]
        public static void Rezoom()
        {
            foreach (RenderWindow window in Engine.windows.Values)
                if (window.ui?.uiRoot is Control root) Rezoom(root);
        }

        private static void Rezoom(Control control)
        {
            if (control is DocumentEditorControl editor)
            {
                if (editor.content == null) return;

                editor.content.zoom = DocumentZoom;
                editor.content.InvalidateLayout();
                editor.RequestScrollToCaret();
                return;
            }

            foreach (Entity child in control.children)
                if (child is Control next) Rezoom(next);
        }
        #endregion

        #region ---- selection ----
        public void CollapseSelection() => content?.CollapseSelection();

        public void SelectAll() => content?.SelectAll();

        public bool DeleteSelection()
        {
            if (!Writable || !content.DeleteSelection()) return false;

            MarkDirty();
            return true;
        }

        public BlockControl CaretBlock => content?.caretBlock;

        // Puts the caret at a block and offset, and scrolls to it.
        public void GoTo(int block, int offset)
        {
            if (content == null) return;

            DocumentAddress at = new DocumentAddress(block, offset);
            content.Select(at, at);
            RequestScrollToCaret();
        }

        // Two clicks take the word, three the visual line.
        internal void SelectLine()
        {
            MoveCaret(CaretMove.LineStart);
            MoveCaret(CaretMove.LineEnd, true);
        }

        internal void BeginSelectionDrag() => StartDrag();

        // Held-button drag: the caret follows the pointer, the anchor stays where the press landed.
        public override void OnDrag(PointerEvent e)
        {
            base.OnDrag(e);
            if (content == null) return;

            AutoScroll(e.point);

            if (content.CaretOffText(e.point, out BlockControl block, out int offset))
                content.SetCaret(block, offset, true);
        }

        // Dragging past the viewport edge scrolls, so a selection can run off-screen. The caret
        // resolves against the geometry this frame still has and catches up on the next tick.
        private void AutoScroll(Vector2 point)
        {
            LayoutRect inner = arrangedRect.Shrink(arrange.padding);

            float overshoot = point.Y < inner.y ? point.Y - inner.y
                            : point.Y > inner.Bottom ? point.Y - inner.Bottom
                            : 0f;
            if (overshoot == 0f) return;

            Vector2 offset = GetScrollOffset();
            SetScrollOffset(new Vector2(offset.X, offset.Y + overshoot * autoScrollRate));
        }
        #endregion

        #region ---- caret movement ----
        public void MoveCaret(CaretMove move, bool extend = false)
        {
            if (content?.caretBlock == null) return;

            if (move == CaretMove.Left) MoveLeft(extend);
            else if (move == CaretMove.Right) MoveRight(extend);
            else if (move == CaretMove.WordLeft || move == CaretMove.WordRight) MoveWord(move == CaretMove.WordLeft ? -1 : 1, extend);
            else if (move == CaretMove.DocumentStart || move == CaretMove.DocumentEnd) MoveToEnd(move == CaretMove.DocumentEnd, extend);
            else MoveToPoint(move, extend);

            RequestScrollToCaret();
        }

        // At a block's edge a word move steps into the neighbour, like a character move does.
        private void MoveWord(int direction, bool extend)
        {
            BlockControl block = content.caretBlock;
            int offset = content.caretOffset;

            if (direction < 0 ? offset == 0 : offset == block.Length)
            {
                if (direction < 0) MoveLeft(extend);
                else MoveRight(extend);
                return;
            }

            content.SetCaret(block, TextInputActions.WordEdge(block.text ?? string.Empty, offset, direction), extend);
        }

        private void MoveToEnd(bool end, bool extend)
        {
            List<BlockControl> blocks = content.Blocks();
            if (blocks.Count == 0) return;

            if (end) content.SetCaret(blocks[^1], blocks[^1].Length, extend);
            else content.SetCaret(blocks[0], 0, extend);
        }

        private void MoveLeft(bool extend)
        {
            if (content.caretOffset > 0)
            {
                content.SetCaret(content.caretBlock, content.caretOffset - 1, extend);
                return;
            }

            BlockControl previous = content.AdjacentBlock(content.caretBlock, -1);
            if (previous != null) content.SetCaret(previous, previous.Length, extend);
        }

        private void MoveRight(bool extend)
        {
            if (content.caretOffset < content.caretBlock.Length)
            {
                content.SetCaret(content.caretBlock, content.caretOffset + 1, extend);
                return;
            }

            BlockControl next = content.AdjacentBlock(content.caretBlock, 1);
            if (next != null) content.SetCaret(next, 0, extend);
        }

        // Up/down, line start/end and page moves are all "resolve this point", because a visual line
        // is a line of the block rather than of the caret.
        private void MoveToPoint(CaretMove move, bool extend)
        {
            if (!content.CaretPoint(out float x, out float y, out float height)) return;

            LayoutRect inner = content.arrangedRect.Shrink(content.arrange.padding);
            float page = arrangedRect.Shrink(arrange.padding).height;

            float targetX = move switch
            {
                CaretMove.LineStart => inner.x,
                CaretMove.LineEnd => inner.x + inner.width,
                _ => x
            };

            float targetY = move switch
            {
                CaretMove.Up => y,
                CaretMove.Down => y + height,
                CaretMove.PageUp => y - page,
                CaretMove.PageDown => y + page,
                _ => y + height * 0.5f
            };

            // Up and down have to exclude the line the caret is already on. Probing just outside it
            // is not enough: blocks are spaced apart, so the current line stays the nearest band to a
            // point one pixel off it and the caret never crosses a block boundary.
            float bandMin = move == CaretMove.Down ? y + height : float.NegativeInfinity;
            float bandMax = move == CaretMove.Up ? y : float.PositiveInfinity;

            if (content.CaretAtPoint(targetX, targetY, out BlockControl block, out int offset, bandMin, bandMax))
                content.SetCaret(block, offset, extend);
        }
        #endregion

        #region ---- editing ----
        public void Backspace(bool word = false) => DeleteOver(word ? CaretMove.WordLeft : CaretMove.Left);

        public void Delete(bool word = false) => DeleteOver(word ? CaretMove.WordRight : CaretMove.Right);

        // Without a selection the caret makes one a character wide, so deleting past a block boundary
        // follows the same rules the arrow keys already resolve.
        private void DeleteOver(CaretMove move)
        {
            if (content?.caretBlock == null || !Writable) return;

            bool backward = move == CaretMove.Left || move == CaretMove.WordLeft;
            using (BeginStep(backward ? "Backspace" : "Delete", InputHandler.firingRepeat))
            {
                if (backward && content.ClearListAtCaret())
                {
                    MarkDirty();
                    return;
                }

                CaretSlot? start = content.HasSelection ? null : content.Focus;
                if (start != null) MoveCaret(move, true);
                if (content.DeleteSelection(start == null)) MarkDirty();
                else if (start is CaretSlot slot) content.SetCaret(slot.block, slot.offset);
            }

            RequestScrollToCaret();
        }

        public void SplitBlock()
        {
            if (!Writable) return;

            using (BeginStep("New paragraph"))
                content.SplitBlock();

            MarkDirty();
            RequestScrollToCaret();
        }

        // One character, recorded against the block it lands in.
        public void TypeChar(char c)
        {
            if (!Writable) return;

            content.TypeChar(c);
            RequestScrollToCaret();
        }
        #endregion

        #region ---- clipboard ----
        public bool Copy()
        {
            content?.CopySelection();
            return content != null;
        }

        public bool Cut()
        {
            if (content == null) return false;
            if (content.SelectedFragment() == null) return true;

            content.CopySelection();
            if (!Writable) return true;
            using (BeginStep("Cut"))
                content.DeleteSelection();

            MarkDirty();
            RequestScrollToCaret();
            return true;
        }

        public bool Paste(string text)
        {
            if (content == null) return false;
            if (!Writable) return true;

            bool pasted;
            using (BeginStep("Paste"))
                pasted = content.PasteText(text);

            if (pasted)
            {
                MarkDirty();
                RequestScrollToCaret();
            }
            return true;
        }

        // A live link to the copied sheet cells; false when the clipboard is not a sheet copy this note can take.
        public bool PasteLink()
        {
            if (content == null || !Writable) return false;
            string? text = ClipboardText.Get();
            if (string.IsNullOrEmpty(text)) return false;

            bool pasted;
            using (BeginStep("Paste link"))
                pasted = content.PasteLink(text);

            if (pasted)
            {
                MarkDirty();
                RequestScrollToCaret();
            }
            return pasted;
        }

        public void RenameSheetLinks(Func<string, string?> rename) => content?.RenameSheetLinks(rename);

        // Saves the picture beside the note under attachments/ and puts it in at the caret.
        public bool PasteImage(Image<Rgba32> image)
        {
            if (content == null || session == null) return false;
            if (!Writable) return true;
            if (Extension is ".txt" or ".tex")
            {
                Log.Info($"a plain text note cannot hold a picture; paste refused.");
                return true;
            }

            string folder = Path.Combine(Path.GetDirectoryName(session.path)!, "attachments");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(session.path)} {DateTime.Now:yyyyMMdd-HHmmss}.png");
            image.SaveAsPng(file);

            bool pasted;
            using (BeginStep("Paste picture"))
                pasted = content.PasteImage(file);

            if (pasted)
            {
                MarkDirty();
                RequestScrollToCaret();
            }
            return true;
        }
        #endregion

        #region ---- text drop ----
        public override bool DraggingOverStart(Control dragged, Vector2 point) => DraggingOver(dragged, point);

        public override bool DraggingOver(Control dragged, Vector2 point)
        {
            DocumentControl source = DocumentControl.TextDragSource(dragged);
            if (!Writable || source == null) return false;

            source.textDragHovered = true;
            AutoScroll(point);
            content.ShowDropAt(point);
            return true;
        }

        public override bool DraggingOverEnd(Control dragged)
        {
            if (DocumentControl.TextDragSource(dragged) != null) content?.HideDrop();
            return false;
        }

        // Takes a text drop from this note or another.
        public override bool FinishDrag(Control dragged, Vector2 point)
        {
            DocumentControl source = DocumentControl.TextDragSource(dragged);
            if (source == null || !Writable) return false;

            content.HideDrop();
            if (!content.CaretOffText(point, out BlockControl block, out int offset)) return true;

            CaretSlot slot = new CaretSlot(block, offset);
            bool copy = InputHandler.instance.IsModifierDown(InputModifier.Copy);
            bool changed;

            if (ReferenceEquals(source, content))
            {
                using (BeginStep(copy ? "Copy" : "Move"))
                    changed = content.DropSelection(slot, copy);
                if (!changed) content.SetCaret(block, offset);
            }
            else
            {
                DocumentFragment fragment = source.SelectedFragment();
                if (fragment == null) return true;

                using (BeginStep("Drop"))
                    content.InsertAt(slot, fragment);
                changed = true;

                if (!copy && source.parent is DocumentEditorControl from)
                    using (from.BeginStep("Move"))
                        from.DeleteSelection();
            }

            if (changed) MarkDirty();
            FocusCaret();
            RequestScrollToCaret();
            return true;
        }
        #endregion

        // Edited, and never given a name. Nothing derives a name from the file, so this stays true
        // until someone answers the prompt.
        public bool needsNaming => session != null && session.isDirty && session.document.name == null;

        public void FocusCaret()
        {
            if (content == null) return;

            UIEngine.SetActiveControl(this);
            content.FocusCaret();
        }

        // The scroll every editing path asks for happens here, because this is the first moment the
        // caret's block has the layout it now lives in.
        //
        // Both branches after ScrollIntoView are load-bearing, and this method must never exit with
        // the arrange flag set. InvalidateArrange bails the moment it meets a control already dirty
        // — and from in here every ancestor is still mid-Arrange — so a flag left set makes the
        // editor permanently dirty and every later invalidate from it is silently dropped.
        protected override void ArrangeCore(LayoutRect finalRect)
        {
            base.ArrangeCore(finalRect);

            if (pendingView != null && content != null)
            {
                SessionTab view = pendingView;
                pendingView = null;

                if (ScrollToView(view)) base.ArrangeCore(finalRect);
                else SetFlag(ArrangeFlags.ArrangeDirty, false);
                return;
            }

            if (!scrollToCaretPending || content == null) return;
            scrollToCaretPending = false;

            if (!content.CaretPoint(out float x, out float y, out float height)) return;

            LayoutRect caretRect = new LayoutRect(x, y, CaretControl.Width, height);
            bool tableMoved = false;
            if (DocumentControl.TableViewport(content.caretBlock) is ScrollableControl viewport)
            {
                Vector2 tableBefore = viewport.GetScrollOffset();
                viewport.ScrollIntoView(caretRect);
                tableMoved = viewport.GetScrollOffset() != tableBefore;
            }

            Vector2 before = GetScrollOffset();
            ScrollIntoView(caretRect);

            if (GetScrollOffset() != before || tableMoved)
            {
                Profiling.Zone.Start("Editor.Rearrange");
                base.ArrangeCore(finalRect);
                Profiling.Zone.End("Editor.Rearrange");
            }
            else SetFlag(ArrangeFlags.ArrangeDirty, false);
        }

        // Deferred to the next Arrange, never done here: a block created this tick has no arranged
        // rect yet, and ScrollIntoView reads a zero rect as "above the viewport" and jumps to the
        // top of the note.
        internal void RequestScrollToCaret()
        {
            scrollToCaretPending = true;
            InvalidateArrange();
        }

        // Where the reader was: caret, selection, the line at the top of the view, the header.
        public SessionTab ViewState()
        {
            if (pendingView != null) return pendingView;

            SessionTab view = new SessionTab();
            if (content == null) return view;

            if (content.caretBlock != null)
            {
                DocumentAddress caret = content.AddressOf(content.caretBlock, content.caretOffset);
                DocumentAddress anchor = content.AnchorAddress;
                (view.caretBlock, view.caretOffset) = (caret.block, caret.offset);
                (view.anchorBlock, view.anchorOffset) = (anchor.block, anchor.offset);
            }

            LayoutRect inner = arrangedRect.Shrink(arrange.padding);
            if (content.CaretAtPoint(inner.x, inner.y, out BlockControl top, out int topOffset))
            {
                DocumentAddress at = content.AddressOf(top, topOffset);
                (view.topBlock, view.topOffset) = (at.block, at.offset);
                view.topDelta = inner.y - (top.TextOrigin.Y + top.CaretAt(topOffset).top);
            }

            view.scrollX = GetScrollOffset().X;
            view.propertiesOpen = content.header is ExpanderControl { expanded: true };
            return view;
        }

        // Puts a ViewState back; the scroll waits for the first Arrange that has lines to measure from.
        public void RestoreView(SessionTab view)
        {
            if (content == null) return;

            content.Select(new DocumentAddress(view.anchorBlock, view.anchorOffset),
                           new DocumentAddress(view.caretBlock, view.caretOffset));
            if (content.header is ExpanderControl expander) expander.expanded = view.propertiesOpen;

            pendingView = view;
            InvalidateArrange();
        }

        private bool ScrollToView(SessionTab view)
        {
            Vector2 before = GetScrollOffset();
            float y = before.Y;

            if (content.Resolve(new DocumentAddress(view.topBlock, view.topOffset), out BlockControl block, out int offset))
            {
                float lineTop = block.TextOrigin.Y + block.CaretAt(offset).top;
                y += lineTop - arrangedRect.Shrink(arrange.padding).y + view.topDelta;
            }

            SetScrollOffset(new Vector2(view.scrollX, y));
            return GetScrollOffset() != before;
        }

        // The gutter and the editor's own padding, which the content does not cover.
        public override bool OnPointerPress(PointerEvent e)
        {
            if (content == null) return false;

            content.DisarmStyle();
            if (content.CaretOffText(e.point, out BlockControl block, out int offset))
                content.SetCaret(block, offset, DocumentControl.Extending);

            return true;
        }

        #region ---- focus ----
        public void OnContextAdded(string context)
        {
            if (context == "ActiveControl") content?.FocusCaret();
        }

        // Raised only when the context went somewhere outside the editor.
        public void OnContextRemoved(string context)
        {
            if (context != "ActiveControl") return;

            for (Control control = UIEngine.activeControl; control != null; control = control.parent as Control)
                if (ReferenceEquals(control, this)) return;

            content?.Blur();
        }
        #endregion
    }
}
