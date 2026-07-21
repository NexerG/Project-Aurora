using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Registry.Assets;
using ArctisAurora.Core.UISystem.Controls;
using ArctisAurora.EngineWork.Registry;
using Silk.NET.Maths;

namespace ArctisAurora.Core.UISystem.Controls.Text
{
    [A_XSDType("ShortText", "UI", AllowedChildren = typeof(IXMLChild_UI), MaxChildren = 1)]
    public class ShortTextControl : TextControl
    {
        public FontAsset fontAsset;

        [A_XSDElementProperty("Text", "UI", "The text to display on the control.")]
        public string text {
            get => field; 
            set
            {
                field = value;
                Dictionary<string, FontAsset> d = AssetRegistries.GetRegistryByValueType<string, FontAsset>(typeof(FontAsset));
                fontAsset = d["default"];

                float horizontalOffset = 0;
                float verticalOffset = 0;

                if (text.Length == 0)
                {
                    return;
                }

                Glyph gAsset;
                for (int i = 0; i < text.Length; i++)
                {
                    gAsset = fontAsset.atlasMetaData.GetGlyph(text[i]);
                    float halfWidth = (gAsset.glyphWidth * fontSize) * 0.5f;
                    horizontalOffset += (gAsset.leftSideOffset * (float)fontSize) + halfWidth;
                    Vector3D<float> glyphPos = transform.position + new Vector3D<float>(horizontalOffset, verticalOffset, 0);
                    GlyphControl glyph = new GlyphControl(text[i], fontAsset, fontSize);
                    AddChild(glyph);

                    horizontalOffset += (gAsset.advanceWidth * (float)fontSize) - halfWidth - (gAsset.leftSideOffset * (float)fontSize);
                }
            }
        } = string.Empty;

        public int fontSize
        {
            get => field;
            set
            {
                field = (int)Math.Ceiling(((float)value * (96f / 72f)));
            }
        } = 72;

        public ShortTextControl()
        {
            controlData.style.tint = new Vector3D<float>(1, 1, 1);
            fontSize = 24;
        }

        public override void BeginEdit()
        {
            throw new NotImplementedException();
        }

        public override void CommitEdit()
        {
            throw new NotImplementedException();
        }

        public override void CancelEdit()
        {
            throw new NotImplementedException();
        }

        public override void WriteChar(char c)
        {
            throw new NotImplementedException();
        }
    }

    /*[A_VulkanControlElement("TextBlock", "Data class for ShortTextControl. Contains properties related to the control's appearance and behavior.")]
    public class ShortTextControlData
    {

    }*/
}
