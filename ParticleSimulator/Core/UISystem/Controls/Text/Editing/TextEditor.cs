/*using ArctisAurora.Core.AssetRegistry;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.Core.UISystem.Controls.Text.Editing
{
    /*public interface IEditableText
    {
        public bool IsEditing { get; set; }
        public void BeginEdit();
        public void CommitEdit();
        public virtual void CancelEdit() { }
        public abstract void WriteChar(char c);
    }*/

    /*public class TextEditor : TextControl
    {

        /*[A_XSDType("InlineRun", "TextEditor")]
        public class InlineRun
        {
            public string text = "";
            public bool bold = false;
            public bool italic = false;
            public bool code = false;
            public bool strikethrough = false;
            public string link = null; // null = no link
        }

        [A_XSDType("BlockType", "TextEditor")]
        public enum BlockType
        {
            Paragraph,
            Heading1, Heading2, Heading3, Heading4, Heading5, Heading6,
            CodeBlock,
            Quote,
            BulletList, NumberedList, ListItem,
            Table, TableRow, TableCell,
            HorizontalRule,
            DocumentEmbed,
            MathBlock,
        }

        [A_XSDType("Block", "TextEditor")]
        public class Block
        {
            public string id;  // stable GUID for cursor addressing
            public BlockType blockType = BlockType.Paragraph;

            // inline content (paragraphs, headings, list items)
            public List<InlineRun> runs = new List<InlineRun>();

            // raw content (code blocks, math blocks)
            public string rawContent = null;

            // metadata
            public string language = null;     // for CodeBlock
            public string embedDocId = null;   // for DocumentEmbed
            public int headingLevel = 1;       // for heading blocks

            // tree structure
            public List<Block> children = new List<Block>();
            public Block parent = null;

            public Block()
            {
                id = Guid.NewGuid().ToString("N")[..12];
            }

            public Block(BlockType type) : this()
            {
                blockType = type;
            }

            public string GetPlainText()
            {
                if (rawContent != null) return rawContent;
                var sb = new System.Text.StringBuilder();
                foreach (var run in runs)
                    sb.Append(run.text);
                return sb.ToString();
            }
        }

        [A_XSDType("DocumentModel", "TextEditor")]
        public class DocumentModel
        {
            public string documentId;
            public string title = "Untitled";
            public string filePath;         // path to .md file
            public List<Block> blocks = new List<Block>();
            public DateTime lastModified = DateTime.Now;

            // forward links discovered during parse
            public List<string> outgoingLinks = new List<string>();
            // back links populated by registry
            public List<string> incomingLinks = new List<string>();

            public DocumentModel()
            {
                documentId = Guid.NewGuid().ToString("N");
            }

            public Block GetBlockById(string blockId)
            {
                return FindBlock(blocks, blockId);
            }

            private Block FindBlock(List<Block> searchBlocks, string blockId)
            {
                foreach (var b in searchBlocks)
                {
                    if (b.id == blockId) return b;
                    var found = FindBlock(b.children, blockId);
                    if (found != null) return found;
                }
                return null;
            }
        }

        public struct CursorPosition
        {
            public string documentId;  // which document
            public string blockId;     // which block
            public int runIndex;       // which inline run
            public int offset;         // character offset within run

            public static CursorPosition Invalid => new CursorPosition
            {
                documentId = null,
                blockId = null,
                runIndex = -1,
                offset = -1
            };
        }

        public class Selection
        {
            public CursorPosition anchor;  // where selection started
            public CursorPosition focus;   // where it currently is (caret)
            public bool IsCollapsed => anchor.blockId == focus.blockId
                                    && anchor.runIndex == focus.runIndex
                                    && anchor.offset == focus.offset;
        }

        public abstract class EditCommand
        {
            public string targetDocumentId;
            public abstract void Execute(DocumentModel doc);
            public abstract void Undo(DocumentModel doc);
        }

        public class InsertTextCommand : EditCommand
        {
            public string blockId;
            public int runIndex;
            public int offset;
            public string text;

            public override void Execute(DocumentModel doc)
            {
                var block = doc.GetBlockById(blockId);
                var run = block.runs[runIndex];
                run.text = run.text.Insert(offset, text);
            }

            public override void Undo(DocumentModel doc)
            {
                var block = doc.GetBlockById(blockId);
                var run = block.runs[runIndex];
                run.text = run.text.Remove(offset, text.Length);
            }
        }

        public class DeleteTextCommand : EditCommand
        {
            public string blockId;
            public int runIndex;
            public int offset;
            public int length;
            public string deletedText; // stored on execute for undo

            public override void Execute(DocumentModel doc)
            {
                var block = doc.GetBlockById(blockId);
                var run = block.runs[runIndex];
                deletedText = run.text.Substring(offset, length);
                run.text = run.text.Remove(offset, length);
            }

            public override void Undo(DocumentModel doc)
            {
                var block = doc.GetBlockById(blockId);
                var run = block.runs[runIndex];
                run.text = run.text.Insert(offset, deletedText);
            }
        }

        public class SplitBlockCommand : EditCommand
        {
            public string blockId;
            public int runIndex;
            public int offset;
            public string newBlockId; // assigned on execute

            public override void Execute(DocumentModel doc)
            {
                // split the block at cursor into two blocks
                // (pressing Enter in the middle of a paragraph)
                var block = doc.GetBlockById(blockId);
                var newBlock = new Block(block.blockType);
                newBlockId = newBlock.id;

                // move content after the split point to the new block
                var currentRun = block.runs[runIndex];
                string afterText = currentRun.text[offset..];
                currentRun.text = currentRun.text[..offset];

                var firstNewRun = new InlineRun { text = afterText };
                newBlock.runs.Add(firstNewRun);

                // move remaining runs
                for (int i = runIndex + 1; i < block.runs.Count; i++)
                    newBlock.runs.Add(block.runs[i]);
                block.runs.RemoveRange(runIndex + 1, block.runs.Count - runIndex - 1);

                // insert new block after current
                int blockIndex = doc.blocks.IndexOf(block);
                doc.blocks.Insert(blockIndex + 1, newBlock);
            }

            public override void Undo(DocumentModel doc)
            {
                // merge the two blocks back together
                var original = doc.GetBlockById(blockId);
                var created = doc.GetBlockById(newBlockId);

                // merge the first run of created back
                if (created.runs.Count > 0)
                {
                    original.runs[^1].text += created.runs[0].text;
                    for (int i = 1; i < created.runs.Count; i++)
                        original.runs.Add(created.runs[i]);
                }
                doc.blocks.Remove(created);
            }
        }

        public class ChangeBlockTypeCommand : EditCommand
        {
            public string blockId;
            public BlockType newType;
            public BlockType previousType;

            public override void Execute(DocumentModel doc)
            {
                var block = doc.GetBlockById(blockId);
                previousType = block.blockType;
                block.blockType = newType;
            }

            public override void Undo(DocumentModel doc)
            {
                var block = doc.GetBlockById(blockId);
                block.blockType = previousType;
            }
        }

        public class UndoStack
        {
            private Stack<EditCommand> undoStack = new Stack<EditCommand>();
            private Stack<EditCommand> redoStack = new Stack<EditCommand>();

            public void Execute(EditCommand cmd, DocumentModel doc)
            {
                cmd.Execute(doc);
                undoStack.Push(cmd);
                redoStack.Clear();
            }

            public void Undo(DocumentModel doc)
            {
                if (undoStack.Count == 0) return;
                var cmd = undoStack.Pop();
                cmd.Undo(doc);
                redoStack.Push(cmd);
            }

            public void Redo(DocumentModel doc)
            {
                if (redoStack.Count == 0) return;
                var cmd = redoStack.Pop();
                cmd.Execute(doc);
                undoStack.Push(cmd);
            }
        }*/

    /*}
}*/