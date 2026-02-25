using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;

namespace ArctisAurora.Core.Rendering.UI.Controls.Text
{
    [A_VulkanControl("ShortText")]
    public class ShortTextControl : VulkanControl
    {
        private string _text = string.Empty;
        public FontAsset fontAsset;

        [A_VulkanControlProperty("Text", "The text to display on the control.")]
        public string text {
            get => _text; 
            set
            {
                _text = value;
                Dictionary<string, FontAsset> d = AssetRegistries.GetRegistry<string, FontAsset>(typeof(FontAsset));
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
                    GlyphControl glyph = new GlyphControl(text[i], glyphPos, fontAsset, fontSize);
                    children.Add(glyph);

                    horizontalOffset += (gAsset.advanceWidth * (float)fontSize) - halfWidth - (gAsset.leftSideOffset * (float)fontSize);
                }
            }
        }

        private int _fontSize = 72;
        public int fontSize {
            get => _fontSize;
            set
            {
                _fontSize = (int)Math.Ceiling(((float)value * (96f/72f)));
            }
        }

        public ShortTextControl()
        {
            controlData.style.tint = new Vector3D<float>(1, 1, 1);
            fontSize = 96;
        }
    }

    /*[A_VulkanControlElement("TextBlock", "Data class for ShortTextControl. Contains properties related to the control's appearance and behavior.")]
    public class ShortTextControlData
    {

    }*/
}
