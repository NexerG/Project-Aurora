namespace ArctisAurora.Core.Security
{
    // An encrypted file opened without a key that decrypts it.
    public sealed class LockedFileException : Exception
    {
        public readonly string path;

        public LockedFileException(string path) : base($"'{path}' is encrypted.") => this.path = path;
    }
}
