using AdvancedRouter;
using hakathon.Editor;
using System.Diagnostics;

namespace hakathon
{
    internal class Program
    {
        static void Main(string[] args)
        {
            bool advancedRouter = args.Any(a => a == "--advanced") || true; // default true
            bool virtualizedRouter = args.Any(a => a == "--virtualized") || true; // default true
            string level = args.FirstOrDefault(a => a.StartsWith("--level="))?.Split('=')[1] ?? "10";

            RunSimulator(args, level, advancedRouter, virtualizedRouter);
        }

        public static void RunSimulator(string[] args, string level, bool advancedRouter, bool virtualizedRouter)
        {
            var dataDir = args.FirstOrDefault(a => a.StartsWith("--dataDir="))?.Split('=')[1] ?? $"./data/{level}/";

            string logFile = args.FirstOrDefault(a => a.StartsWith("--eventLogFile="))?.Split('=')[1]
                ?? (advancedRouter? $"./simulationAdvancedRouter{level}.log"
                    : $"./simulationDefaultRouter{level}.log");

            string routerCmd = args.FirstOrDefault(a => a.StartsWith("--router="))?.Split('=')[1]
                ?? (advancedRouter
                    ? "./build/AdvancedRouter/AdvancedRouter.exe"
                    : "./build/router.exe");

            var (shipments, bins, grids, truckSchedules, parameters) = SimulationLoader.Load(dataDir);
            var simulator = new Simulator(shipments, bins, grids, truckSchedules, parameters, virtualizedRouter);
            simulator.InitLog(logFile);
            simulator.InitRouter(routerCmd);

            int waitSeconds = int.TryParse(
                args.FirstOrDefault(a => a.StartsWith("--wait="))?.Split('=')[1],
                out int w) ? w : 0;

            if (waitSeconds > 0)
            {
                Console.Error.WriteLine($"Starting in {waitSeconds} seconds...");
                Thread.Sleep(waitSeconds * 1000);
            }
            simulator.Run();
        }
    }
}
