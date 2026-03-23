using hakathon.Editor;
using System.Text.Json.Serialization;

namespace AdvancedRouter
{
    public class RouterInput
    {
        [JsonPropertyName("state")] public RouterState State { get; set; } = new();
    }

    public class VirtualRouterState
    {
        public DateTime Now { get; set; }
        public List<Shipment> ShipmentsBacklog { get; set; } = new();
        public List<Bin> StockBins { get; set; } = new();
        public List<Grid> Grids { get; set; } = new();
        public List<TruckScheduleDto> TruckSchedules { get; set; } = new();
    }

    public class RouterState
    {
        [JsonPropertyName("now")] public DateTime Now { get; set; }
        [JsonPropertyName("shipments_backlog")] public List<RouterShipment> ShipmentsBacklog { get; set; } = new();
        [JsonPropertyName("stock_bins")] public List<RouterBin> StockBins { get; set; } = new();
        [JsonPropertyName("truck_arrival_schedules")] public RouterTruckSchedules TruckArrivalSchedules { get; set; } = new();
        [JsonPropertyName("grids")] public List<RouterGrid> Grids { get; set; } = new();
    }

    public class RouterShipment
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("items")] public Dictionary<string, int> Items { get; set; } = new();
        [JsonPropertyName("handling_flags")] public List<string> HandlingFlags { get; set; } = new();
        [JsonPropertyName("sorting_direction")] public string SortingDirection { get; set; } = string.Empty;
    }

    public class RouterBin
    {
        [JsonPropertyName("bin_id")] public string BinId { get; set; } = string.Empty;
        [JsonPropertyName("grid_id")] public string GridId { get; set; } = string.Empty;
        [JsonPropertyName("items")] public Dictionary<string, int> Items { get; set; } = new();
    }

    public class RouterGrid
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("shifts")] public List<RouterShift> Shifts { get; set; } = new();
    }

    public class RouterShift
    {
        [JsonPropertyName("start_at")] public DateTime StartAt { get; set; }
        [JsonPropertyName("end_at")] public DateTime EndAt { get; set; }
        [JsonPropertyName("port_config")] public List<RouterPortConfig> PortConfig { get; set; } = new();
    }

    public class RouterPortConfig
    {
        [JsonPropertyName("port_id")] public string? PortId { get; set; }
        [JsonPropertyName("handling_flags")] public List<string> HandlingFlags { get; set; } = new();
    }

    public class RouterTruckSchedules
    {
        [JsonPropertyName("schedules")] public List<RouterTruckSchedule> Schedules { get; set; } = new();
    }

    public class RouterTruckSchedule
    {
        [JsonPropertyName("sortingDirection")] public string SortingDirection { get; set; } = string.Empty;
        [JsonPropertyName("pullTimes")] public List<string> PullTimes { get; set; } = new();
        [JsonPropertyName("weekdays")] public List<string> Weekdays { get; set; } = new();
    }

    // Output
    public class RouterOutput
    {
        [JsonPropertyName("assignments")] public List<Assignment> Assignments { get; set; } = new();
    }

    public class Assignment
    {
        [JsonPropertyName("shipment_id")] public string ShipmentId { get; set; } = string.Empty;
        [JsonPropertyName("priority")] public int Priority { get; set; }
        [JsonPropertyName("packing_grid")] public string PackingGrid { get; set; } = string.Empty;
        [JsonPropertyName("picks")] public List<Pick> Picks { get; set; } = new();
    }

    public class Pick
    {
        [JsonPropertyName("ean")] public string Ean { get; set; } = string.Empty;
        [JsonPropertyName("bin_id")] public string BinId { get; set; } = string.Empty;
        [JsonPropertyName("qty")] public int Qty { get; set; }
    }
}
