using ArctisAurora.Core.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace ArctisAurora.Core.Filing
{
    // A picture on the OS clipboard: a PNG stream, a device-independent bitmap, or a copied image file.
    public static class ClipboardImage
    {
        private static readonly LogChannel Log = LogChannel.For("Filing");

        // standard clipboard formats
        private const uint dib = 8;
        private const uint hDrop = 15;
        private const uint dibV5 = 17;

        // BITMAPINFOHEADER fields
        private const int fileHeaderSize = 14;
        private const uint bitFields = 3;

        private static readonly string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tga", ".tif", ".tiff" };

        public static bool TryGet([NotNullWhen(true)] out Image<Rgba32>? image)
        {
            image = null;
            if (!OpenClipboard(IntPtr.Zero))
            {
                Log.Warn($"the clipboard is held by another application.");
                return false;
            }

            try
            {
                uint png = RegisterClipboardFormatW("PNG");
                if (png != 0 && Read(png) is byte[] pngBytes)
                    image = Image.Load<Rgba32>(pngBytes);
                else if (Read(dibV5) is byte[] v5)
                    image = FromDib(v5);
                else if (Read(dib) is byte[] plain)
                    image = FromDib(plain);
                else if (DroppedImage() is string path)
                    image = Image.Load<Rgba32>(path);
            }
            catch (Exception e)
            {
                Log.Warn($"clipboard picture failed to decode: {e.Message}");
                image = null;
            }
            finally
            {
                CloseClipboard();
            }

            return image != null;
        }

        // A clipboard format's bytes, or null when it is absent.
        private static byte[]? Read(uint format)
        {
            if (!IsClipboardFormatAvailable(format)) return null;

            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;

            IntPtr data = GlobalLock(handle);
            if (data == IntPtr.Zero) return null;

            try
            {
                byte[] bytes = new byte[(int)GlobalSize(handle)];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }

        // Prefixes a BITMAPFILEHEADER so the BMP decoder reads it.
        private static Image<Rgba32> FromDib(byte[] packed)
        {
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(packed.AsSpan(0));
            ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(packed.AsSpan(14));
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(packed.AsSpan(16));
            uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(packed.AsSpan(32));

            uint masks = headerSize == 40 && compression == bitFields ? 12u : 0u;
            uint palette = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1u << bitCount : 0u;
            uint pixelOffset = fileHeaderSize + headerSize + masks + palette * 4;

            byte[] file = new byte[fileHeaderSize + packed.Length];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)file.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), pixelOffset);
            packed.CopyTo(file, fileHeaderSize);

            Image<Rgba32> image = Image.Load<Rgba32>(file);
            if (bitCount == 32) OpaqueIfAlphaUnused(image);
            return image;
        }

        // A 32-bit DIB whose alpha is all zero means no alpha, not a transparent picture.
        private static void OpaqueIfAlphaUnused(Image<Rgba32> image)
        {
            bool anyAlpha = false;
            image.ProcessPixelRows(rows =>
            {
                for (int y = 0; y < rows.Height && !anyAlpha; y++)
                    foreach (Rgba32 pixel in rows.GetRowSpan(y))
                        if (pixel.A != 0) { anyAlpha = true; break; }
            });
            if (anyAlpha) return;

            image.ProcessPixelRows(rows =>
            {
                for (int y = 0; y < rows.Height; y++)
                    foreach (ref Rgba32 pixel in rows.GetRowSpan(y))
                        pixel.A = 255;
            });
        }

        // The first image file among copied files.
        private static string? DroppedImage()
        {
            if (!IsClipboardFormatAvailable(hDrop)) return null;

            IntPtr drop = GetClipboardData(hDrop);
            if (drop == IntPtr.Zero) return null;

            uint count = DragQueryFileW(drop, uint.MaxValue, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFileW(drop, i, null, 0);
                StringBuilder path = new StringBuilder((int)length + 1);
                DragQueryFileW(drop, i, path, length + 1);

                string candidate = path.ToString();
                if (imageExtensions.Contains(Path.GetExtension(candidate).ToLowerInvariant()) && File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormatW(string name);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr handle);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder? file, uint length);
    }
}
