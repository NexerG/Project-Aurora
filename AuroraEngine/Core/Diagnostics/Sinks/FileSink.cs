namespace ArctisAurora.Core.Diagnostics.Sinks
{
    // The log file, opened shared-for-read so it can be tailed while the engine runs, and with the
    // stream's own buffer off because the spool already batches.
    internal sealed class FileSink : IDisposable
    {
        private readonly string _directory;
        private readonly string _name;
        private readonly long _maxBytes;
        private readonly int _keep;

        private FileStream? _stream;
        private long _written;

        public string Path => System.IO.Path.Combine(_directory, _name + ".log");

        public FileSink(string directory, string name, long maxBytes, int keep)
        {
            _directory = directory;
            _name = name;
            _maxBytes = maxBytes;
            _keep = keep;
        }

        public bool Write(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return true;

            try
            {
                if (_stream == null && !Open()) return false;
                if (_maxBytes > 0 && _written + bytes.Length > _maxBytes) Roll();

                _stream!.Write(bytes);
                _stream.Flush();
                _written += bytes.Length;
                return true;
            }
            catch (Exception)
            {
                // A logger that throws takes the application with it. Drop the sink instead.
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }

        private bool Open()
        {
            Directory.CreateDirectory(_directory);
            _stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 1);
            _written = _stream.Length;
            return true;
        }

        // engine.log -> engine.1.log -> ... -> engine.{keep}.log, oldest dropped.
        private void Roll()
        {
            _stream!.Dispose();
            _stream = null;

            for (int i = _keep; i >= 1; i--)
            {
                string older = System.IO.Path.Combine(_directory, $"{_name}.{i}.log");
                string newer = i == 1 ? Path : System.IO.Path.Combine(_directory, $"{_name}.{i - 1}.log");

                if (!File.Exists(newer)) continue;
                if (File.Exists(older)) File.Delete(older);
                File.Move(newer, older);
            }

            Open();
        }
    }
}
