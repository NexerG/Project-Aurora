using OpenTK.Graphics.OpenGL4;
using ArctisAurora.EngineWork.Rendering;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork.Model
{
    public class Texture
    {
        int texture;
        public Texture()
        {
            ImageResult image;
            using (FileStream stream = File.OpenRead("../../../Shaders/Brick2.png"))
            {
                image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
            //because STBI reads from bot left to bot right, whislt OpenGL renders from top left to bot right
            StbImage.stbi_set_flip_vertically_on_load(1);
            GL.GenTextures(1, out texture);                     //how many texture objeccts to generate
            GL.ActiveTexture(TextureUnit.Texture0);             //activate said texture object
            GL.BindTexture(TextureTarget.Texture2D, texture);   //bind our picture to the object

            //texture filtering settings
            int[] MagFilter = { (int)TextureMagFilter.Nearest };
            GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, MagFilter);
            GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, MagFilter);
            int[] WrapFilter = { (int)TextureWrapMode.Repeat };
            GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, WrapFilter);
            GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, WrapFilter);

            //applying texture size to object and gen mipmap for viewving at distances/angles
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.Byte, image.Data);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Bind()
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D,texture);
        }
        public void texUnit(ShaderClass shader, string uniform, int unit)
        {
            int texUni = GL.GetUniformLocation(shader.program, uniform);
            GL.Uniform1(texUni, unit);
        }
    }
}
