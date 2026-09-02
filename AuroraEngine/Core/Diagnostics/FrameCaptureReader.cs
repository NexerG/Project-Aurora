using System.Globalization;
using System.Xml;

namespace ArctisAurora.Core.Diagnostics
{
    // one timed span of a captured frame, in ticks from the frame's start plus its own bytes
    public struct CapturedSpan
    {
        public int name;
        public long begin;
        public long end;
        public long bytes;
        public int depth;
    }

    // one counter's tally against the span index it fired inside, -1 for the frame itself
    public struct CapturedCounter
    {
        public int span;
        public int name;
        public long value;
    }

    // one frame's slice of its thread's span and counter arrays
    public struct CapturedFrame
    {
        public long index;
        public long start;
        public long duration;
        public long bytes;
        public int firstSpan;
        public int spanCount;
        public int firstCounter;
        public int counterCount;
    }

    // One thread's frame file, read whole. Spans stay in document order, so a frame's slice is
    // already nested and needs no reconstruction.
    public sealed class CapturedThread
    {
        // header
        public string thread = string.Empty;
        public long frequency;
        public DateTime started;
        public string mode = string.Empty;
        public int requested;
        public int dropped;
        public bool truncated;

        public readonly List<string> names = new List<string>();
        public readonly List<CapturedFrame> frames = new List<CapturedFrame>();
        public readonly List<CapturedSpan> spans = new List<CapturedSpan>();
        public readonly List<CapturedCounter> counters = new List<CapturedCounter>();

        public double Ms(long ticks) => frequency > 0 ? ticks * 1000.0 / frequency : 0;

        public string NameOf(int index) => index >= 0 && index < names.Count ? names[index] : "?";

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2}MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2}GB";
        }
    }

    // A session folder as it looks on disk, before anything is parsed.
    public sealed class CaptureSessionInfo
    {
        public string directory = string.Empty;
        public string name = string.Empty;
        public string[] threads = Array.Empty<string>();
    }

    // A folder of per-thread frame files. Every thread's <F T> is the same process-wide clock, so
    // they share one timeline.
    public sealed class CaptureSession
    {
        public string directory = string.Empty;
        public string name = string.Empty;
        public readonly List<CapturedThread> threads = new List<CapturedThread>();
    }

    // The reader for what FrameSpool writes. A file whose process died has no closing tag, so a
    // truncated document is an expected outcome and not an error.
    public static class FrameCaptureReader
    {
        private static readonly LogChannel Log = LogChannel.For("Profiling");

        private static readonly XmlReaderSettings readerSettings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
            CloseInput = true,
        };

        // Session folders under a capture root, newest first. Nothing is parsed.
        public static List<CaptureSessionInfo> Enumerate(string root)
        {
            List<CaptureSessionInfo> sessions = new List<CaptureSessionInfo>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return sessions;

            foreach (string directory in Directory.GetDirectories(root))
            {
                string[] files = Directory.GetFiles(directory, "*.frames.xml");
                if (files.Length == 0) continue;

                sessions.Add(new CaptureSessionInfo
                {
                    directory = directory,
                    name = Path.GetFileName(directory),
                    threads = files.Select(ThreadName).OrderBy(t => t, StringComparer.Ordinal).ToArray(),
                });
            }

            sessions.Sort((a, b) => StringComparer.Ordinal.Compare(b.name, a.name));
            return sessions;
        }

        // Every *.frames.xml in one session folder.
        public static CaptureSession LoadSession(string directory)
        {
            CaptureSession session = new CaptureSession
            {
                directory = directory,
                name = Path.GetFileName(directory),
            };

            if (!Directory.Exists(directory)) return session;

            foreach (string file in Directory.GetFiles(directory, "*.frames.xml").OrderBy(f => f, StringComparer.Ordinal))
            {
                CapturedThread? thread = LoadFile(file);
                if (thread != null) session.threads.Add(thread);
            }
            return session;
        }

        // One thread's file. What was read before a truncation is kept; the frame it died inside
        // is not, since nothing points at its spans.
        public static CapturedThread? LoadFile(string path)
        {
            CapturedThread thread = new CapturedThread { thread = ThreadName(path) };

            int[] open = new int[32];
            int depth = 0;
            bool inFrame = false;
            CapturedFrame frame = default;

            try
            {
                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using XmlReader reader = XmlReader.Create(stream, readerSettings);

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.LocalName == "Z") depth--;
                        else if (reader.LocalName == "F")
                        {
                            Commit(thread, ref frame);
                            inFrame = false;
                        }
                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element) continue;

                    switch (reader.LocalName)
                    {
                        case "FrameCapture":
                            ReadHeader(thread, reader);
                            break;

                        case "N":
                            ReadName(thread, reader);
                            break;

                        case "Batch":
                            thread.dropped += Int(reader, "Dropped");
                            break;

                        case "F":
                            frame = new CapturedFrame
                            {
                                index = Long(reader, "I"),
                                start = Long(reader, "T"),
                                duration = Long(reader, "D"),
                                bytes = Long(reader, "A"),
                                firstSpan = thread.spans.Count,
                                firstCounter = thread.counters.Count,
                            };
                            depth = 0;
                            inFrame = true;
                            if (reader.IsEmptyElement)
                            {
                                Commit(thread, ref frame);
                                inFrame = false;
                            }
                            break;

                        case "Z":
                            if (depth == open.Length) Array.Resize(ref open, open.Length * 2);
                            thread.spans.Add(new CapturedSpan
                            {
                                name = Int(reader, "N"),
                                begin = Long(reader, "B"),
                                end = Long(reader, "E"),
                                bytes = Long(reader, "A"),
                                depth = depth,
                            });
                            if (!reader.IsEmptyElement) open[depth++] = thread.spans.Count - 1;
                            break;

                        case "C":
                            thread.counters.Add(new CapturedCounter
                            {
                                span = depth > 0 ? open[depth - 1] : -1,
                                name = Int(reader, "N"),
                                value = Long(reader, "V"),
                            });
                            break;
                    }
                }
            }
            catch (XmlException)
            {
                thread.truncated = true;
                if (inFrame)
                {
                    thread.spans.RemoveRange(frame.firstSpan, thread.spans.Count - frame.firstSpan);
                    thread.counters.RemoveRange(frame.firstCounter, thread.counters.Count - frame.firstCounter);
                }
            }
            catch (Exception exception)
            {
                Log.Error($"frame capture read failed — {path}: {exception.Message}");
                return null;
            }

            return thread;
        }

        private static void Commit(CapturedThread thread, ref CapturedFrame frame)
        {
            frame.spanCount = thread.spans.Count - frame.firstSpan;
            frame.counterCount = thread.counters.Count - frame.firstCounter;
            thread.frames.Add(frame);
        }

        private static void ReadHeader(CapturedThread thread, XmlReader reader)
        {
            thread.thread = reader.GetAttribute("Thread") ?? thread.thread;
            thread.frequency = Long(reader, "Frequency");
            thread.mode = reader.GetAttribute("Mode") ?? string.Empty;
            thread.requested = Int(reader, "Requested");

            string? started = reader.GetAttribute("Started");
            if (started != null)
                DateTime.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out thread.started);
        }

        // The writer declares names in dense runs, but the index is what the body cites.
        private static void ReadName(CapturedThread thread, XmlReader reader)
        {
            int index = Int(reader, "I");
            if (index < 0) return;

            while (thread.names.Count <= index) thread.names.Add(string.Empty);
            thread.names[index] = reader.GetAttribute("V") ?? string.Empty;
        }

        // "Main.frames.xml" is the Main thread, matching the name half rule of every data file.
        private static string ThreadName(string path)
        {
            string file = Path.GetFileName(path);
            int dot = file.IndexOf('.');
            return dot < 0 ? file : file.Substring(0, dot);
        }

        private static long Long(XmlReader reader, string name) =>
            long.TryParse(reader.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

        private static int Int(XmlReader reader, string name) =>
            int.TryParse(reader.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
