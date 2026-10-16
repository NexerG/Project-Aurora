using System.Text;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace ArctisAurora.Core.Security
{
    // Windows Hello as a key source: a TPM-held credential signs a fixed per-vault challenge, and the same signature comes back every time.
    public static class WindowsHello
    {
        public static async Task<bool> IsAvailable()
        {
            try
            {
                return await KeyCredentialManager.IsSupportedAsync();
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The signature over the vault's challenge, making the credential first when create is set; null when Hello is cancelled or missing.
        public static async Task<byte[]?> Sign(string credential, string vaultId, bool create)
        {
            KeyCredentialRetrievalResult opened = create
                ? await KeyCredentialManager.RequestCreateAsync(credential, KeyCredentialCreationOption.ReplaceExisting)
                : await KeyCredentialManager.OpenAsync(credential);
            if (opened.Status != KeyCredentialStatus.Success) return null;

            byte[] challenge = Encoding.UTF8.GetBytes("Aurora.Vault.Hello." + vaultId);
            KeyCredentialOperationResult signed = await opened.Credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
            if (signed.Status != KeyCredentialStatus.Success) return null;

            CryptographicBuffer.CopyToByteArray(signed.Result, out byte[] signature);
            return signature;
        }

        public static async Task Delete(string credential)
        {
            try
            {
                await KeyCredentialManager.DeleteAsync(credential);
            }
            catch (Exception)
            {
            }
        }
    }
}
