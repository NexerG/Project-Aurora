using System.Text.Json;
using System.Text.Json.Serialization;

namespace hakathon.Editor
{
    // DTOs matching JSON shapes exactly
    public class BinDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("currentGridLocation")] public string GridId { get; set; } = string.Empty;
        [JsonPropertyName("itemsInBin")] public Dictionary<string, BinItemDto> ItemsInBin { get; set; } = new();
    }
    public class BinItemDto
    {
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
    }

    public class FlexiblePortIndexConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt32().ToString(),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                _ => null
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    public class PortConfigDto
    {
        [JsonPropertyName("id")] public string? PortId { get; set; }
        
        [JsonPropertyName("portIndex")]
        [JsonConverter(typeof(FlexiblePortIndexConverter))]
        public string? PortIndex { get; set; }
        [JsonPropertyName("handlingFlags")] public List<string> HandlingFlags { get; set; } = new();
    }

    public class GridDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("shifts")] public List<ShiftDto> Shifts { get; set; } = new();
    }

    public class ShipmentDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("items")] public Dictionary<string, int> Items { get; set; } = new();
        [JsonPropertyName("shipmentDate")] public DateTime ShipmentDate { get; set; }
        [JsonPropertyName("handlingFlags")] public List<string>? HandlingFlags { get; set; }
        [JsonPropertyName("sortingDirection")] public string? SortingDirection { get; set; }
    }

    public class ParametersDto
    {
        [JsonPropertyName("routerIntervalSeconds")] public double RouterIntervalSeconds { get; set; } = 900;
        [JsonPropertyName("pickingThroughput")] public PickingThroughputDto PickingThroughput { get; set; } = new();
        [JsonPropertyName("gridBinDelivery")] public GridBinDeliveryDto GridBinDelivery { get; set; } = new();
        [JsonPropertyName("transfersConveyors")] public TransfersConveyorsDto TransfersConveyors { get; set; } = new();
        [JsonPropertyName("truckArrivalSchedules")] public TruckArrivalSchedulesDto TruckArrivalSchedules { get; set; } = new();

        // keep these for backward compatibility with levels 1-7
        public double StandardThroughput => PickingThroughput.Standard;
        public double FragileThroughput => PickingThroughput.Fragile;
        public double PickRandomnessMin => PickingThroughput.Randomness.Min;
        public double PickRandomnessMax => PickingThroughput.Randomness.Max;
        public int PortQueueCapacity => GridBinDelivery.PortQueueCapacity;

        [JsonPropertyName("simulation_start_time")] public DateTime SimulationStartTime { get; set; } = new DateTime(2026, 3, 1, 7, 0, 0, DateTimeKind.Utc);
        [JsonPropertyName("simulation_end_time")] public DateTime SimulationEndTime { get; set; } = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
    }

    public class GridBinDeliveryDto
    {
        [JsonPropertyName("portQueueCapacity")] public int PortQueueCapacity { get; set; } = 20;
        [JsonPropertyName("deliveryTimes")] public Dictionary<string, double> DeliveryTimes { get; set; } = new();
        [JsonPropertyName("randomness")] public RandomnessDto Randomness { get; set; } = new();
    }

    public class TransferDurationDto
    {
        [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
        [JsonPropertyName("to")] public string To { get; set; } = string.Empty;
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("throughput")] public double Throughput { get; set; }
    }

    public class TransfersConveyorsDto
    {
        [JsonPropertyName("durations")] public List<TransferDurationDto> Durations { get; set; } = new();
        [JsonPropertyName("durationRandomness")] public RandomnessDto DurationRandomness { get; set; } = new();
        [JsonPropertyName("throughputRandomness")] public RandomnessDto ThroughputRandomness { get; set; } = new();
    }

    public class RandomnessDto
    {
        [JsonPropertyName("min")] public double Min { get; set; } = 0.8;
        [JsonPropertyName("max")] public double Max { get; set; } = 1.2;
    }

    public class PickingThroughputDto
    {
        [JsonPropertyName("standard")] public double Standard { get; set; } = 140;
        [JsonPropertyName("fragile")] public double Fragile { get; set; } = 70;
        [JsonPropertyName("randomness")] public RandomnessDto Randomness { get; set; } = new();
    }

    public class ShiftDto
    {
        [JsonPropertyName("start")] public string ShiftStart { get; set; } = string.Empty;
        [JsonPropertyName("end")] public string ShiftEnd { get; set; } = string.Empty;
        [JsonPropertyName("breaks")] public List<BreakScheduleDto> Breaks { get; set; }
        [JsonPropertyName("portConfig")] public List<PortConfigDto> ShiftPortConfig { get; set; } = new();
    }

    public class RouterResponse
    {
        [JsonPropertyName("assignments")]
        public List<AssignmentDto> Assignments { get; set; } = new();
    }

    public class AssignmentDto
    {
        [JsonPropertyName("shipment_id")] public string ShipmentId { get; set; } = string.Empty;
        [JsonPropertyName("priority")] public int Priority { get; set; }
        [JsonPropertyName("packing_grid")] public string PackingGrid { get; set; } = string.Empty;
        [JsonPropertyName("picks")] public List<PickDto> Picks { get; set; } = new();
    }

    public class PickDto
    {
        [JsonPropertyName("ean")] public string Ean { get; set; } = string.Empty;
        [JsonPropertyName("bin_id")] public string BinId { get; set; } = string.Empty;
        [JsonPropertyName("qty")] public int Qty { get; set; }
    }

    public class TruckScheduleDto
    {
        [JsonPropertyName("sortingDirection")] public string SortingDirection { get; set; } = string.Empty;
        [JsonPropertyName("pullTimes")] public List<string> PullTimes { get; set; } = new();
        [JsonPropertyName("weekdays")] public List<string> Weekdays { get; set; } = new();
    }

    public class TruckArrivalSchedulesDto
    {
        [JsonPropertyName("schedules")] public List<TruckScheduleDto> Schedules { get; set; } = new();
    }
}
