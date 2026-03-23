using System.Diagnostics;
using System.Text.Json;

namespace AdvancedRouter
{
    public class RouterProgram
    {
        static void Main(string[] args)
        {
            try
            {
                string input = Console.In.ReadToEnd();
                Console.Error.WriteLine($"Received input length: {input.Length}");

                var routerInput = JsonSerializer.Deserialize<RouterInput>(input);
                Console.Error.WriteLine($"Backlog: {routerInput?.State.ShipmentsBacklog.Count ?? -1}");

                if (routerInput == null)
                {
                    Console.Error.WriteLine("Failed to parse input");
                    Console.WriteLine("{\"assignments\":[]}");
                    return;
                }

                if (routerInput == null)
                {
                    Console.Error.WriteLine("Failed to parse input");
                    Console.WriteLine("{\"assignments\":[]}");
                    return;
                }

                var router = new CAdvancedRouter(routerInput.State);
                var routerStart = Stopwatch.GetTimestamp();
                var output = router.Route();
                var routerElapsed = Stopwatch.GetElapsedTime(routerStart);
                Console.Error.WriteLine($"Router inside took: {routerElapsed.TotalMilliseconds:F0}ms");
                Console.WriteLine(JsonSerializer.Serialize(output));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Router crashed: {ex}");
                Console.WriteLine("{\"assignments\":[]}");
            }
        }
    }
}
