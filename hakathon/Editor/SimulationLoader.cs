using System.Text.Json;
using System.Text.Json.Serialization;

namespace hakathon.Editor
{
    public static class SimulationLoader
    {
        public static (List<Shipment>, List<Bin>, List<Grid>, List<TruckScheduleDto>, ParametersDto) Load(string dataDir)
        {
            var bins = LoadJson<List<BinDto>>(dataDir, "bins.json")
                                .Select(MapBin).ToList();
            var gridDtos = LoadJson<List<GridDto>>(dataDir, "grids.json");
            var shipments = LoadJson<List<ShipmentDto>>(dataDir, "shipments.json")
                                .Select(MapShipment).ToList();
            var parameters = File.Exists(Path.Combine(dataDir, "parameters.json"))
                ? LoadJson<ParametersDto>(dataDir, "parameters.json")
                : LoadJson<ParametersDto>(dataDir, "params.json");

            Console.WriteLine($"DataDir: {dataDir}");
            Console.WriteLine($"Shipments loaded: {shipments.Count}");
            Console.WriteLine($"Bins loaded: {bins.Count}");

            if (parameters.SimulationStartTime == default)
                parameters.SimulationStartTime = shipments.Min(s => s.CreatedAt);

            if (parameters.SimulationEndTime == default ||
                parameters.SimulationEndTime < shipments.Max(s => s.CreatedAt).AddDays(1))
                parameters.SimulationEndTime = shipments.Max(s => s.CreatedAt).AddDays(1);

            var grids = gridDtos.Select(g => MapGrid(g, parameters)).ToList();
            var truckSchedules = parameters.TruckArrivalSchedules.Schedules;

            ValidateShipments(shipments, grids, bins);

            return (shipments, bins, grids, truckSchedules, parameters);
        }

        private static void ValidateShipments(List<Shipment> shipments, List<Grid> grids, List<Bin> bins)
        {
            var allPortFlags = grids
                .SelectMany(g => g.Shifts)
                .SelectMany(s => s.ShiftPortConfig)
                .Select(p => p.HandlingFlags)
                .ToList();

            // pre-index total stock per EAN across all bins
            var totalStock = new Dictionary<string, int>();
            foreach (var bin in bins)
                foreach (var (ean, qty) in bin.Stock)
                    totalStock[ean] = totalStock.GetValueOrDefault(ean) + qty;

            int noPort = 0;
            int noStock = 0;
            int partialStock = 0;

            foreach (var shipment in shipments)
            {
                // check port compatibility
                var operationalFlags = shipment.HandlingFlags
                    .Where(f => f != "priority")
                    .ToList();

                if (operationalFlags.Count > 0)
                {
                    bool hasCompatiblePort = allPortFlags.Any(portFlags =>
                        portFlags.Count > 0 &&
                        operationalFlags.All(f => portFlags.Contains(f)));

                    if (!hasCompatiblePort)
                        noPort++;
                }

                // check stock
                foreach (var (ean, qty) in shipment.Items)
                {
                    int available = totalStock.GetValueOrDefault(ean);
                    if (available == 0)
                    {
                        noStock++;
                        break;
                    }
                    if (available < qty)
                    {
                        partialStock++;
                        break;
                    }
                }
            }

            Console.Error.WriteLine($"Validation: {shipments.Count} shipments, {noPort} no compatible port, {noStock} no stock, {partialStock} insufficient stock");
        }


        private static T LoadJson<T>(string dataDir, string fileName) where T : new()
        {
            string path = Path.Combine(dataDir, fileName);
            if (!File.Exists(path))
                return new T();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json) ?? new T();
        }


        private static Bin MapBin(BinDto dto) => new Bin
        {
            Id = dto.Id,
            GridId = dto.GridId,
            Stock = dto.ItemsInBin.ToDictionary(k => k.Key, v => v.Value.Quantity),
            Status = BinStatus.Available
        };

        private static Shipment MapShipment(ShipmentDto dto) => new Shipment
        {
            Id = dto.Id,
            CreatedAt = dto.ShipmentDate,
            Items = dto.Items,
            HandlingFlags = dto.HandlingFlags ?? new List<string>(),
            SortingDirection = dto.SortingDirection ?? string.Empty,
            Status = ShipmentStatus.Received
        };

        private static Grid MapGrid(GridDto dto, ParametersDto parameters)
        {
            var grid = new Grid
            {
                Id = dto.Id,
                Shifts = dto.Shifts,
                Ports = dto.Shifts
                    .SelectMany(s => s.ShiftPortConfig)
                    .GroupBy(p => p.PortId ?? $"{dto.Id}-{p.PortIndex ?? "0"}")
                    .Select(g => new Port
                    {
                        Id = g.Key,
                        GridId = dto.Id,
                        HandlingFlags = g.First().HandlingFlags,
                        QueueCapacity = parameters.PortQueueCapacity,
                        Status = PortStatus.Idle
                    }).ToList()
            };

            grid.AverageBinDeliveryTime = dto.Id switch
            {
                "AS1" => 6,
                "AS2" => 4,
                "AS3" => 5,
                _ => 5
            };

            return grid;
        }
    }
}