using System.Globalization;
using System.Xml.Linq;

namespace ArctisAurora.Core.Testing
{
    public sealed class TestFailure
    {
        public string message = string.Empty;
        public string file = string.Empty;
        public int line;
    }

    public sealed class TestShot
    {
        public string name = string.Empty;
        public string result = string.Empty;
        public string golden = string.Empty;
        public string? actual;
        public string? diff;
    }

    public sealed class TestResult
    {
        public string suite = string.Empty;
        public string name = string.Empty;
        public string result = string.Empty;
        public int ticks;
        public string reason = string.Empty;
        public string? capture;
        public readonly List<TestFailure> failures = new List<TestFailure>();
        public readonly List<TestShot> shots = new List<TestShot>();
    }

    public sealed class TestRunInfo
    {
        public string directory = string.Empty;
        public string name = string.Empty;
        public string host = string.Empty;
        public string build = string.Empty;
        public DateTime started;
        public int passed;
        public int failed;
        public int skipped;
        public int newCount;
    }

    public sealed class TestRun
    {
        public TestRunInfo info = new TestRunInfo();
        public readonly List<TestResult> tests = new List<TestResult>();
    }

    // The reader for the results.xml TestRunner writes.
    public static class TestResultsReader
    {
        public const string fileName = "results.xml";

        // Run folders under a Tests root, newest first.
        public static List<TestRunInfo> Enumerate(string root)
        {
            List<TestRunInfo> runs = new List<TestRunInfo>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return runs;

            foreach (string directory in Directory.GetDirectories(root))
            {
                TestRun? run = Load(directory);
                if (run != null) runs.Add(run.info);
            }

            runs.Sort((a, b) => StringComparer.Ordinal.Compare(b.name, a.name));
            return runs;
        }

        // One run folder; null when it holds no readable results.xml.
        public static TestRun? Load(string directory)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return null;

            XElement root;
            try
            {
                root = XElement.Load(path);
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }

            TestRun run = new TestRun();
            run.info.directory = directory;
            run.info.name = Path.GetFileName(directory);
            run.info.host = (string?)root.Attribute("Host") ?? string.Empty;
            run.info.build = (string?)root.Attribute("Build") ?? string.Empty;
            DateTime.TryParse((string?)root.Attribute("Started"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out run.info.started);

            foreach (XElement element in root.Elements("Test"))
            {
                TestResult test = new TestResult
                {
                    suite = (string?)element.Attribute("Suite") ?? string.Empty,
                    name = (string?)element.Attribute("Name") ?? string.Empty,
                    result = (string?)element.Attribute("Result") ?? string.Empty,
                    ticks = (int?)element.Attribute("Ticks") ?? 0,
                    reason = (string?)element.Attribute("Reason") ?? string.Empty,
                };
                string? capture = (string?)element.Attribute("Capture");
                if (capture != null) test.capture = Path.Combine(directory, capture);

                foreach (XElement failure in element.Elements("Failure"))
                    test.failures.Add(new TestFailure
                    {
                        message = (string?)failure.Attribute("Message") ?? string.Empty,
                        file = (string?)failure.Attribute("File") ?? string.Empty,
                        line = (int?)failure.Attribute("Line") ?? 0,
                    });

                foreach (XElement shot in element.Elements("Shot"))
                {
                    string? actual = (string?)shot.Attribute("Actual");
                    string? diff = (string?)shot.Attribute("Diff");
                    test.shots.Add(new TestShot
                    {
                        name = (string?)shot.Attribute("Name") ?? string.Empty,
                        result = (string?)shot.Attribute("Result") ?? string.Empty,
                        golden = (string?)shot.Attribute("Golden") ?? string.Empty,
                        actual = actual == null ? null : Path.Combine(directory, actual),
                        diff = diff == null ? null : Path.Combine(directory, diff),
                    });
                }

                if (test.result == "Pass") run.info.passed++;
                else if (test.result == "Skipped") run.info.skipped++;
                else if (test.result == "New") run.info.newCount++;
                else run.info.failed++;

                run.tests.Add(test);
            }
            return run;
        }
    }
}
