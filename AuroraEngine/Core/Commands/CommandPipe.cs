using ArctisAurora.Core.Diagnostics;
using ArctisAurora.EngineWork;
using System.IO.Pipes;
using System.Reflection;

namespace ArctisAurora.Core.Commands
{
    // \\.\pipe\Aurora.<Host>: a running host takes one line per connection and answers with its replies. --send is the
    // client, run from the same exe before any engine boots.
    internal static class CommandPipe
    {
        private static readonly LogChannel Log = LogChannel.For("Console");

        private const int connectTimeoutMs = 2000;
        private const int replyTimeoutMs = 60000;

        private static string Name => "Aurora." + (Assembly.GetEntryAssembly()?.GetName().Name ?? "Engine");

        internal static void Serve() => new Thread(Loop) { Name = "command pipe", IsBackground = true }.Start();

        private static void Loop()
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                }
                catch (IOException exception)
                {
                    Log.Warn($"{Name} is already served — {exception.Message}");
                    return;
                }

                using (pipe)
                {
                    try
                    {
                        pipe.WaitForConnection();
                        Answer(pipe);
                    }
                    catch (IOException) { }
                }
            }
        }

        private static void Answer(NamedPipeServerStream pipe)
        {
            string? line = new StreamReader(pipe).ReadLine();
            if (line == null) return;

            TaskCompletionSource<string> replied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Engine.Post(() => CommandConsole.Execute(line, reply => replied.TrySetResult(reply)));
            string reply = replied.Task.Wait(replyTimeoutMs) ? replied.Task.Result : $"error: no reply within {replyTimeoutMs} ms";

            StreamWriter writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.Write(reply);
            pipe.WaitForPipeDrain();
        }

        // Prints the running host's replies to line. Exit code: 0 all answered, 1 an error answered, 2 no host running.
        internal static int Send(string line)
        {
            using NamedPipeClientStream pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            try
            {
                pipe.Connect(connectTimeoutMs);
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine($"no running {Name} to send to");
                return 2;
            }

            new StreamWriter(pipe) { AutoFlush = true }.WriteLine(line);
            string reply = new StreamReader(pipe).ReadToEnd();
            Console.WriteLine(reply);
            return reply.Split('\n').Any(r => r.StartsWith("error:", StringComparison.Ordinal)) ? 1 : 0;
        }
    }
}
