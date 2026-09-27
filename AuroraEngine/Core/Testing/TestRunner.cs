using ArctisAurora.Core.Diagnostics;
using ArctisAurora.Core.ECS.EngineEntity;
using ArctisAurora.Core.Filing.Serialization;
using ArctisAurora.Core.Registry;
using ArctisAurora.Core.Threading;
using ArctisAurora.EngineWork;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace ArctisAurora.Core.Testing
{
    [A_XSDType("Test", "Test")]
    public class TestEntry
    {
        [A_XSDElementProperty("Action", "Test")]
        public Action? action { get; set; }

        [A_XSDElementProperty("Timeout", "Test")]
        public int timeout { get; set; } = TestRunner.defaultTimeoutMs;
    }

    // --test runs every Data/XML/Documents/Tests/<Suite>.tests.xml, --test=<Suite> the one named, and exits
    // with the failure count.
    [A_XSDType("TestSuite", "Test")]
    internal static class TestRunner
    {
        private static readonly LogChannel Log = LogChannel.For("Test");

        [A_XSDElementProperty("Test", "Test")]
        public static List<TestEntry> tests { get; set; } = new();

        internal const int defaultTimeoutMs = 10000;

        // the fixed clock, and how long Boot runs the host's own tree
        private const double step = 1.0 / 60.0;
        private const int bootTicks = 60;

        public static bool active { get; private set; }

        private static readonly Dictionary<string, MethodInfo> _actions = new();
        private static readonly List<(string suite, string action, int timeoutMs)> _queue = new();
        private static readonly List<Result> _results = new();
        private static string _resultsRoot = string.Empty;
        private static DateTime _started;

        private sealed class Result
        {
            public string suite = string.Empty;
            public string name = string.Empty;
            public int ticks;
            public List<(string message, string file, int line)> failures = new();
        }

        public static void Arm()
        {
            string? filter = null;
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "--test") active = true;
                else if (arg.StartsWith("--test=", StringComparison.Ordinal))
                {
                    active = true;
                    filter = arg.Substring("--test=".Length);
                }
            }
            if (!active) return;

            Engine.fixedStep = step;
            _started = DateTime.Now;

            string? hostRoot = SettingsRegistry.WriteRoot;
            string? parent = hostRoot == null ? null : Directory.GetParent(hostRoot)?.FullName;
            _resultsRoot = Path.Combine(parent ?? AppContext.BaseDirectory, "Tests");
            if (parent != null)
            {
                string settings = Path.Combine(parent, "TestSettings");
                if (Directory.Exists(settings)) Directory.Delete(settings, true);
                SettingsRegistry.SetWriteRoot(settings);
            }

            Discover();
            Load(filter);
            Engine.Post(() => new Session());
        }

        private static void Discover()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                foreach (Type type in assembly.GetTypes())
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        A_XSDActionDependencyAttribute? attr = method.GetCustomAttribute<A_XSDActionDependencyAttribute>();
                        if (attr != null && attr.Category == "Test")
                            _actions[attr.Name] = method;
                    }
        }

        private static void Load(string? filter)
        {
            bool found = false;
            foreach (string path in VirtualFileSystem.EnumerateAll("XML/Documents/Tests", "*.tests.xml"))
            {
                string suite = Paths.DocName(path);
                if (filter != null && !string.Equals(suite, filter, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;

                XElement root = XElement.Load(path);
                XNamespace ns = root.GetDefaultNamespace();
                foreach (XElement test in root.Elements(ns + "Test"))
                {
                    string? action = test.Attribute("Action")?.Value;
                    if (action == null) continue;
                    int timeoutMs = int.TryParse(test.Attribute("Timeout")?.Value, out int ms) ? ms : defaultTimeoutMs;
                    _queue.Add((suite, action, timeoutMs));
                }
            }

            if (filter != null && !found)
                Record(filter, filter, 0, new() { ($"no suite named {filter}", "", 0) });
        }

        private static void Record(string suite, string name, int ticks, List<(string message, string file, int line)> failures)
        {
            _results.Add(new Result { suite = suite, name = name, ticks = ticks, failures = failures });

            string label = suite.Length == 0 ? name : $"{suite}/{name}";
            if (failures.Count == 0)
                Log.Info($"PASS {label} ({ticks} ticks)");
            else
                Log.Warn($"FAIL {label} — {string.Join("; ", failures.Select(Describe))}");
        }

        private static string Describe((string message, string file, int line) failure)
            => failure.file.Length == 0 ? failure.message : $"{failure.message} ({failure.file}:{failure.line})";

        private static void Finish()
        {
            int failed = _results.Count(r => r.failures.Count > 0);
            string path = Write();
            Log.Info($"{_results.Count - failed} passed, {failed} failed — {path}");
            Environment.ExitCode = failed;
            Engine.Post(Shutdown.Request);
        }

        private static string Write()
        {
            XElement run = new XElement("TestRun",
                new XAttribute("Host", Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty),
                new XAttribute("Build", Engine.isDebug ? "Debug" : "Release"),
                new XAttribute("Started", _started.ToString("o", CultureInfo.InvariantCulture)));

            foreach (Result result in _results)
                run.Add(new XElement("Test",
                    new XAttribute("Suite", result.suite),
                    new XAttribute("Name", result.name),
                    new XAttribute("Result", result.failures.Count == 0 ? "Pass" : "Fail"),
                    new XAttribute("Ticks", result.ticks),
                    result.failures.Select(f => new XElement("Failure",
                        new XAttribute("Message", f.message),
                        new XAttribute("File", f.file),
                        new XAttribute("Line", f.line)))));

            string stamp = _started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string directory = Path.Combine(_resultsRoot, stamp);
            for (int i = 2; Directory.Exists(directory); i++)
                directory = Path.Combine(_resultsRoot, stamp + "-" + i.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "results.xml");
            run.Save(path);
            return path;
        }

        // Runs Boot, then each queued test to its end.
        private sealed class Session : Entity
        {
            private int _boot;
            private int _index = -1;
            private (string suite, string action, int timeoutMs) _current;
            private TestContext _context = null!;
            private IEnumerator<int>? _test;
            private int _ticks;
            private int _wait;
            private int _errorsAtStart;
            private bool _finished;

            public Session()
            {
                SettingsRegistry.Get<ThreadingSettings>().idle.wait = false;
                SetTicking(true);
            }

            public override void OnTick()
            {
                base.OnTick();
                if (_finished) return;

                if (_boot < bootTicks)
                {
                    if (++_boot == bootTicks) EndBoot();
                    return;
                }

                if (_test == null && !StartNext())
                {
                    _finished = true;
                    Finish();
                    return;
                }

                Step();
            }

            private void EndBoot()
            {
                int errors = Volatile.Read(ref LogSpool.errorCount);
                List<(string message, string file, int line)> failures = new();
                if (errors > 0) failures.Add(($"{errors} error(s) logged since launch", "", 0));
                Record("", "Boot", bootTicks, failures);
            }

            // Starts the next queued test that resolves; false once the queue is done.
            private bool StartNext()
            {
                while (++_index < _queue.Count)
                {
                    _current = _queue[_index];
                    if (!_actions.TryGetValue(_current.action, out MethodInfo? method))
                    {
                        Record(_current.suite, _current.action, 0, new() { ($"no test named {_current.action}", "", 0) });
                        continue;
                    }

                    _context = new TestContext();
                    _ticks = 0;
                    _wait = 0;
                    _errorsAtStart = Volatile.Read(ref LogSpool.errorCount);
                    try
                    {
                        _test = (IEnumerator<int>)method.Invoke(null, new object[] { _context })!;
                        return true;
                    }
                    catch (Exception exception)
                    {
                        End(exception);
                    }
                }
                return false;
            }

            private void Step()
            {
                _ticks++;
                if (_ticks * step * 1000.0 > _current.timeoutMs)
                {
                    End(null, $"timed out after {_current.timeoutMs} ms");
                    return;
                }
                if (--_wait > 0) return;

                bool running;
                try
                {
                    running = _test!.MoveNext();
                }
                catch (Exception exception)
                {
                    End(exception);
                    return;
                }

                if (running) _wait = Math.Max(_test.Current, 1);
                else End(null);
            }

            // Records the test that just ended, with whatever failed it.
            private void End(Exception? exception, string? reason = null)
            {
                List<(string message, string file, int line)> failures = new(_context.failures);
                if (reason != null) failures.Add((reason, "", 0));
                if (exception != null)
                {
                    StackFrame? frame = new StackTrace(exception, true).GetFrame(0);
                    failures.Add(($"{exception.GetType().Name}: {exception.Message}",
                        Path.GetFileName(frame?.GetFileName() ?? ""), frame?.GetFileLineNumber() ?? 0));
                }
                int errors = Volatile.Read(ref LogSpool.errorCount) - _errorsAtStart;
                if (errors > 0) failures.Add(($"{errors} error(s) logged", "", 0));

                Record(_current.suite, _current.action, _ticks, failures);
                if (exception != null) Log.Exception(LogLevel.Warn, exception, $"{_current.action} threw");
                _test = null;
            }
        }
    }
}
