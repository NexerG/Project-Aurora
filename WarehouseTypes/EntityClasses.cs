using System.Text.Json.Serialization;

namespace hakathon.Editor
{
    public static class Helpers
    {
        public static DateTime CombineDateAndTimeForSim(DateTime date, string time)
        {
            var parts = time.Split(':');
            int hours = int.Parse(parts[0]);
            int minutes = int.Parse(parts[1]);

            DateTime result = new DateTime(date.Year, date.Month, date.Day,
                                           hours, minutes, 0, DateTimeKind.Utc);

            return result;
        }
    }

    public enum ShipmentStatus
    {
        Received, Routed, Consolidation, Ready, Picking, Packed, Shipped, Failed
    }

    public enum BinStatus
    {
        Available, Reserved, Outside
    }

    public enum PortStatus
    {
        Closed, Idle, Busy, PendingClose
    }

    public class Shipment
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("items")] public Dictionary<string, int> Items { get; set; } = new();
        [JsonPropertyName("handling_flags")] public List<string> HandlingFlags { get; set; } = new();
        [JsonPropertyName("sorting_direction")] public string SortingDirection { get; set; } = string.Empty;

        // simulator-only fields
        public ShipmentStatus Status { get; set; } = ShipmentStatus.Received;
        public DateTime? ShippedTime { get; set; }
        public DateTime? CompletionTime { get; set; }
        public string PackingGridId { get; set; } = string.Empty;
        public List<PickItem> Picks { get; set; } = new();
        public bool IsPriority => HandlingFlags.Contains("priority");
        public bool IsFragile => HandlingFlags.Contains("fragile");
    }

    public class PickItem
    {
        public string Ean { get; set; } = string.Empty;
        public string BinId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public bool Completed { get; set; } = false;
    }
    public class Bin
    {
        public string Id { get; set; }
        public string GridId { get; set; }
        public Dictionary<string, int> Stock { get; set; } = new(); // EAN -> quantity
        public BinStatus Status { get; set; } = BinStatus.Available;
        public string? ReservedForPortId { get; set; }
        public Queue<string> WaitingPorts { get; set; } = new(); // FCFS waiting list
        public Dictionary<string, int> ReservedQuantities { get; set; } = new();
    }

    public class Port
    {
        public string Id { get; set; }
        public string GridId { get; set; }
        public List<string> HandlingFlags { get; set; } = new();
        public PortStatus Status { get; set; } = PortStatus.Idle;
        public LinkedList<Shipment> ShipmentQueue { get; set; } = new();
        public int QueueCount => ShipmentQueue.Count;
        public int QueueCapacity { get; set; } = 20;
        public Shipment? CurrentShipment { get; set; }
        public Bin? CurrentBin { get; set; }
        public BreakScheduleDto? CurrentBreak { get; set; }
        public bool CanAccept(Shipment shipment)
        {
            if (ShipmentQueue.Count >= QueueCapacity) return false;

            var operationalFlags = shipment.HandlingFlags
                .Where(f => f != "priority")
                .ToList();

            if (operationalFlags.Count == 0) return true;
            if (HandlingFlags.Count == 0) return false; // general port can't handle flagged shipments
            return operationalFlags.All(f => HandlingFlags.Contains(f));
        }
    }

    public class Grid
    {
        public string Id { get; set; }
        public List<Port> Ports { get; set; } = new();
        public List<Bin> Bins { get; set; } = new();
        public LinkedList<Shipment> GridQueue { get; set; } = new();
        public List<ShiftDto> Shifts { get; set; } = new();
        public double AverageBinDeliveryTime { get; set; }
        public double DeliveryRandomnessMin { get; set; } = 0.8;
        public double DeliveryRandomnessMax { get; set; } = 1.2;
    }
    public class BreakScheduleDto
    {
        [JsonPropertyName("start")] public string BreakStart { get; set; } = string.Empty;
        [JsonPropertyName("end")] public string BreakEnd { get; set; } = string.Empty;
    }
}