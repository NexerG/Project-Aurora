using ArctisAurora.Core.AssetRegistry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text.Editing
{
    [A_XSDType("TextInput", "UI", typeof(GlyphControl))]
    public class TextInputControl : TextControl, IContext
    {
        public override void BeginEdit()
        {
            isEditing = true;
            Console.WriteLine("SET EDITING");
        }

        public override void CancelEdit()
        {}

        public override void CommitEdit()
        {}

        public override void WriteChar(char c)
        {
            text += c;
        }

        public override void ResolveOnClick(Vector2D<float> oldPos, Vector2D<float> delta)
        {
            BeginEdit();
        }

        public void OnContextAdded()
        {}

        public void OnContextRemoved()
        {
            isEditing = false;
            Console.WriteLine("SET FALSE");
        }
    }
}
