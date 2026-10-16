using System.Security.Cryptography;
using System.Text;

namespace ArctisAurora.Core.Security
{
    // AES-256-GCM over bytes; blob = version | alg | salt | ciphertext | tag, the subkey and nonce derived from the salt.
    public static class Cipher
    {
        public const int keySize = 32;

        // blob layout
        private const byte version = 1;
        private const byte aesGcm = 1;
        private const int saltSize = 32;
        private const int nonceSize = 12;
        private const int tagSize = 16;
        private const int headerSize = 2 + saltSize;
        public const int overhead = headerSize + tagSize;

        private static readonly byte[] info = Encoding.ASCII.GetBytes("Aurora.Cipher.v1");

        public static byte[] NewKey() => RandomNumberGenerator.GetBytes(keySize);

        public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input, ReadOnlySpan<byte> associated = default)
        {
            byte[] blob = new byte[overhead + input.Length];
            blob[0] = version;
            blob[1] = aesGcm;
            Span<byte> salt = blob.AsSpan(2, saltSize);
            RandomNumberGenerator.Fill(salt);

            Span<byte> derived = stackalloc byte[keySize + nonceSize];
            Derive(key, salt, derived);
            using (AesGcm aes = new AesGcm(derived[..keySize], tagSize))
                aes.Encrypt(derived[keySize..], input, blob.AsSpan(headerSize, input.Length), blob.AsSpan(headerSize + input.Length, tagSize), associated);
            CryptographicOperations.ZeroMemory(derived);
            return blob;
        }

        public static bool TryDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> blob, out byte[] output, ReadOnlySpan<byte> associated = default)
        {
            output = Array.Empty<byte>();
            if (blob.Length < overhead || blob[0] != version || blob[1] != aesGcm) return false;

            int length = blob.Length - overhead;
            byte[] plain = new byte[length];
            Span<byte> derived = stackalloc byte[keySize + nonceSize];
            Derive(key, blob.Slice(2, saltSize), derived);
            try
            {
                using AesGcm aes = new AesGcm(derived[..keySize], tagSize);
                aes.Decrypt(derived[keySize..], blob.Slice(headerSize, length), blob.Slice(headerSize + length, tagSize), plain, associated);
            }
            catch (AuthenticationTagMismatchException)
            {
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
            }
            output = plain;
            return true;
        }

        private static void Derive(ReadOnlySpan<byte> key, ReadOnlySpan<byte> salt, Span<byte> derived)
        {
            if (key.Length != keySize) throw new ArgumentException($"Cipher keys are {keySize} bytes, got {key.Length}.");
            HKDF.DeriveKey(HashAlgorithmName.SHA256, key, derived, salt, info);
        }
    }
}
