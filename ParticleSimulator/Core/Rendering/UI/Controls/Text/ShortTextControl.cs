using ArctisAurora.EngineWork.AssetRegistry;
using ArctisAurora.EngineWork.Rendering.UI;
using ArctisAurora.EngineWork.Rendering.UI.Controls;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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
                for (int i = 0; i < text.Length; i++)
                {
                    Glyph gAsset = fontAsset.atlasMetaData.GetGlyph(text[i]);
                    horizontalOffset += (gAsset.lsb * fontSize);
                    verticalOffset = (gAsset.tsb * fontSize);
                    Vector3D<float> glyphPos = transform.position + new Vector3D<float>(horizontalOffset, verticalOffset, 0);
                    GlyphControl glyph = new GlyphControl(text[i], glyphPos, gAsset, fontAsset, fontSize);
                    children.Add(glyph);

                    horizontalOffset += (gAsset.rsb * fontSize);
                }
            }
        }

        public int fontSize { get; set; } = 72;

        public ShortTextControl()
        {
            controlData.style.tint = new Vector3D<float>(1, 1, 1);
        }
    }

    /*[A_VulkanControlElement("TextBlock", "Data class for ShortTextControl. Contains properties related to the control's appearance and behavior.")]
    public class ShortTextControlData
    {

    }*/
}
