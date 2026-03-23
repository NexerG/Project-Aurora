using hakathon.Editor;
using System.Diagnostics;

namespace AdvancedRouter
{
    public class CAdvancedRouter
    {
        //private readonly RouterState _state;

        // track reserved quantities across assignments in this run
        private Dictionary<string, Dictionary<string, int>> _reservedStock = new();
        private Dictionary<string, Dictionary<string, List<Bin>>> _binsByGridAndEan = new();
        private Dictionary<string, Dictionary<string, HashSet<Bin>>> _binsHashset = new();
        //private bool _binIndexDirty = true;
        //public void InvalidateBinIndex() => _binIndexDirty = true;

        private DateTime _now;
        private List<Shipment> _shipments;
        private List<Bin> _bins;
        private List<Grid> _grids;
        private List<TruckScheduleDto> _truckSchedules;

        public CAdvancedRouter(RouterState state)
        {
            _now = state.Now;
            _shipments = state.ShipmentsBacklog.Select(s => new Shipment
            {
                Id = s.Id,
                CreatedAt = s.CreatedAt,
                Items = s.Items,
                HandlingFlags = s.HandlingFlags,
                SortingDirection = s.SortingDirection
            }).ToList();

            _bins = state.StockBins.Select(b => new Bin
            {
                Id = b.BinId,
                GridId = b.GridId,
                Stock = b.Items
            }).ToList();

            _grids = state.Grids.Select(g => new Grid
            {
                Id = g.Id,
                Shifts = g.Shifts.Select(s => new ShiftDto
                {
                    ShiftStart = s.StartAt.ToString("HH:mm"),
                    ShiftEnd = s.EndAt.ToString("HH:mm"),
                    Breaks = new List<BreakScheduleDto>(),
                    ShiftPortConfig = s.PortConfig.Select(p => new PortConfigDto
                    {
                        PortId = p.PortId,
                        HandlingFlags = p.HandlingFlags
                    }).ToList()
                }).ToList()
            }).ToList();

            _truckSchedules = state.TruckArrivalSchedules.Schedules.Select(t => new TruckScheduleDto
            {
                SortingDirection = t.SortingDirection,
                PullTimes = t.PullTimes,
                Weekdays = t.Weekdays
            }).ToList();
        }

        // virtualized - direct types
        public CAdvancedRouter(List<Grid> grids, List<TruckScheduleDto> schedules)
        {
            _grids = grids;
            _truckSchedules = schedules;
        }

        public void Update(DateTime now, List<Shipment> backlog, List<Bin> bins, Dictionary<string, Dictionary<string, HashSet<Bin>>> byEan)
        {
            _now = now;
            _shipments = backlog;
            _bins = bins;
            _binsHashset = byEan;
        }

        public RouterResponse RouteVirtual()
        {
            var assignments = new List<AssignmentDto>();

            // step 1 - prioritize shipments
            var prioritized = PrioritizeShipments();

            // step 2 - determine capacity per grid
            var capacities = ComputeGridCapacities();

            // step 3 - assign each shipment
            var shipmentTime = Stopwatch.GetTimestamp();
            int priorityCounter = prioritized.Count;
            foreach (var shipment in prioritized)
            {
                var assignment = TryAssignVirtual(shipment, capacities);
                if (assignment == null) continue;

                assignment.Priority = priorityCounter--;
                assignments.Add(assignment);
            }
            var shipmentElapsed = Stopwatch.GetElapsedTime(shipmentTime);
            Console.Error.WriteLine($"shipment: {shipmentElapsed.TotalMilliseconds:F0}ms");

            return new RouterResponse { Assignments = assignments };
        }


        public RouterResponse Route()
        {
            var assignments = new List<AssignmentDto>();

            // step 1 - prioritize shipments
            var prioritized = PrioritizeShipments();

            // step 2 - determine capacity per grid
            var capacities = ComputeGridCapacities();

            // step 3 - assign each shipment
            //var shipmentTime = Stopwatch.GetTimestamp();
            int priorityCounter = prioritized.Count;
            foreach (var shipment in prioritized)
            {
                var assignment = TryAssign(shipment, capacities);
                if (assignment == null) continue;

                assignment.Priority = priorityCounter--;
                assignments.Add(assignment);
            }
            //var shipmentElapsed = Stopwatch.GetElapsedTime(shipmentTime);
            //Console.Error.WriteLine($"shipment: {shipmentElapsed.TotalMilliseconds:F0}ms");

            return new RouterResponse { Assignments = assignments };
        }

        private List<Shipment> PrioritizeShipments()
        {
            var nearDeadlineDirs = GetNearDeadlineDirections();

            return _shipments
                .OrderByDescending(s => s.HandlingFlags.Contains("priority"))
                .ThenByDescending(s => nearDeadlineDirs.Contains(NormalizeSortingDirection(s.SortingDirection)))
                .ThenBy(s => s.CreatedAt)
                .ToList();
        }

        private HashSet<string> GetNearDeadlineDirections()
        {
            var result = new HashSet<string>();
            var now = _now;
            foreach (var schedule in _truckSchedules)
            {
                if (!schedule.Weekdays.Contains(now.DayOfWeek.ToString())) continue;
                foreach (var pullTime in schedule.PullTimes)
                {
                    var parts = pullTime.Split(':');
                    var truckTime = new DateTime(now.Year, now.Month, now.Day,
                        int.Parse(parts[0]), int.Parse(parts[1]), 0, DateTimeKind.Utc);
                    if (truckTime > now && (truckTime - now).TotalHours <= 5)
                        result.Add(NormalizeSortingDirection(schedule.SortingDirection));
                }
            }
            return result;
        }

        private Dictionary<string, (int Packing, int Consolidation)> ComputeGridCapacities()
        {
            var capacities = new Dictionary<string, (int, int)>();
            foreach (var grid in _grids)
            {
                int activePorts = 0;
                foreach (var shift in grid.Shifts)
                {
                    var startAt = Helpers.CombineDateAndTimeForSim(_now.Date, shift.ShiftStart);
                    var endAt = Helpers.CombineDateAndTimeForSim(_now.Date, shift.ShiftEnd);
                    if (endAt <= startAt) endAt = endAt.AddDays(1);
                    if (startAt <= _now && endAt >= _now)
                        activePorts += shift.ShiftPortConfig.Count;
                }
                capacities[grid.Id] = (activePorts * 25, activePorts * 25);
            }
            return capacities;
        }

        private bool dictBuilt = false;
        private AssignmentDto? TryAssign(Shipment shipment,
            Dictionary<string, (int Packing, int Consolidation)> capacities)
        {
            var eligible = _grids
                .Where(g => HasCompatiblePort(g, shipment) && capacities.ContainsKey(g.Id))
                .ToList();

            if (!eligible.Any()) return null;

            if (!dictBuilt)
            {
                dictBuilt = true;
                //var dictionaryStart = Stopwatch.GetTimestamp();
                //var dictionaryBins1Start = Stopwatch.GetTimestamp();

                foreach (var bin in _bins)
                {
                    if(bin.Status != BinStatus.Available) continue;
                    if (!_binsByGridAndEan.TryGetValue(bin.GridId, out var eanMap))
                    {
                        eanMap = new Dictionary<string, List<Bin>>();
                        _binsByGridAndEan[bin.GridId] = eanMap;
                    }
                    foreach (var ean in bin.Stock.Keys)
                    {
                        if (!eanMap.TryGetValue(ean, out var list))
                        {
                            list = new List<Bin>();
                            eanMap[ean] = list;
                        }
                        list.Add(bin);
                    }
                }

                //var bins1Elapsed = Stopwatch.GetElapsedTime(dictionaryBins1Start);
                //var binstotal = Stopwatch.GetElapsedTime(dictionaryStart);
                //Console.Error.WriteLine($"binning 1: {bins1Elapsed.TotalMilliseconds:F0}ms");
                //Console.Error.WriteLine($"binning total: {binstotal.TotalMilliseconds:F0}ms");
            }

            // score each grid by transfer cost
            var scored = eligible
                    .Select(g => (Grid: g, Cost: ComputeTransferCost(shipment, g.Id)))
                    .Where(x => x.Cost >= 0) // -1 means impossible, skip entirely
                    .OrderBy(x => x.Cost)
                    .ToList();

            if (!scored.Any()) return null;

            var best = scored.First();
            bool needsConsolidation = best.Cost > 0;

            if (needsConsolidation && capacities[best.Grid.Id].Consolidation <= 0) return null;
            if (!needsConsolidation && capacities[best.Grid.Id].Packing <= 0) return null;

            // only call BuildPicks if we know fulfillment is possible
            var picks = BuildPicks(shipment, best.Grid.Id);
            if (picks == null) return null;

            // update capacity
            if (needsConsolidation)
                capacities[best.Grid.Id] = (capacities[best.Grid.Id].Packing, capacities[best.Grid.Id].Consolidation - 1);
            else
                capacities[best.Grid.Id] = (capacities[best.Grid.Id].Packing - 1, capacities[best.Grid.Id].Consolidation);

            return new AssignmentDto
            {
                ShipmentId = shipment.Id,
                PackingGrid = best.Grid.Id,
                Picks = picks
            };
        }

        private AssignmentDto? TryAssignVirtual(Shipment shipment,
            Dictionary<string, (int Packing, int Consolidation)> capacities)
        {
            var eligible = _grids
                .Where(g => HasCompatiblePort(g, shipment) && capacities.ContainsKey(g.Id))
                .ToList();

            if (!eligible.Any()) return null;

            // score each grid by transfer cost
            var scored = eligible
                    .Select(g => (Grid: g, Cost: ComputeTransferCost(shipment, g.Id)))
                    .Where(x => x.Cost >= 0) // -1 means impossible, skip entirely
                    .OrderBy(x => x.Cost)
                    .ToList();

            if (!scored.Any()) return null;

            var best = scored.First();
            bool needsConsolidation = best.Cost > 0;

            if (needsConsolidation && capacities[best.Grid.Id].Consolidation <= 0) return null;
            if (!needsConsolidation && capacities[best.Grid.Id].Packing <= 0) return null;

            // only call BuildPicks if we know fulfillment is possible
            var picks = BuildPicksVirtual(shipment, best.Grid.Id);
            if (picks == null) return null;

            // update capacity
            if (needsConsolidation)
                capacities[best.Grid.Id] = (capacities[best.Grid.Id].Packing, capacities[best.Grid.Id].Consolidation - 1);
            else
                capacities[best.Grid.Id] = (capacities[best.Grid.Id].Packing - 1, capacities[best.Grid.Id].Consolidation);

            return new AssignmentDto
            {
                ShipmentId = shipment.Id,
                PackingGrid = best.Grid.Id,
                Picks = picks
            };
        }



        private bool HasCompatiblePort(Grid grid, Shipment shipment)
        {
            var operationalFlags = shipment.HandlingFlags
                .Where(f => f != "priority")
                .ToList();

            foreach (var shift in grid.Shifts)
            {
                var startAt = Helpers.CombineDateAndTimeForSim(_now.Date, shift.ShiftStart);
                var endAt = Helpers.CombineDateAndTimeForSim(_now.Date, shift.ShiftEnd);
                if (endAt <= startAt) endAt = endAt.AddDays(1);
                if (startAt > _now || endAt < _now) continue;

                foreach (var p in shift.ShiftPortConfig)
                {
                    if (operationalFlags.Count == 0) return true;
                    if (p.HandlingFlags.Count == 0) continue; // general port, skip for flagged shipments
                    if (operationalFlags.All(f => p.HandlingFlags.Contains(f))) return true;
                }
            }
            return false;
        }

        private int ComputeTransferCost(Shipment shipment, string packingGridId)
        {
            int cost = 0;
            foreach (var (ean, qty) in shipment.Items)
            {
                int remaining = qty;
                bool needsTransfer = false;

                if (_binsByGridAndEan.TryGetValue(packingGridId, out var localEans) &&
                    localEans.TryGetValue(ean, out var localBins))
                {
                    foreach (var bin in localBins)
                    {
                        int available = bin.Stock[ean] - GetReserved(bin.Id, ean);
                        remaining -= Math.Min(available, remaining);
                        if (remaining <= 0) break;
                    }
                }

                if (remaining > 0)
                {
                    foreach (var (gridId, eanMap) in _binsByGridAndEan)
                    {
                        if (gridId == packingGridId) continue;
                        if (!eanMap.TryGetValue(ean, out var otherBins)) continue;
                        foreach (var bin in otherBins)
                        {
                            int available = bin.Stock[ean] - GetReserved(bin.Id, ean);
                            if (available > 0) needsTransfer = true;
                            remaining -= Math.Min(available, remaining);
                            if (remaining <= 0) break;
                        }
                        if (remaining <= 0) break;
                    }
                }

                if (remaining > 0) return -1;
                if (needsTransfer) cost++;
            }
            return cost;
        }

        private List<PickDto>? BuildPicks(Shipment shipment, string packingGridId)
        {
            var picks = new List<PickDto>();
            foreach (var (ean, qty) in shipment.Items)
            {
                int remaining = qty;

                if (_binsByGridAndEan.TryGetValue(packingGridId, out var localEans) &&
                    localEans.TryGetValue(ean, out var localBins))
                {
                    foreach (var bin in localBins)
                    {
                        if (remaining <= 0) break;
                        int available = bin.Stock[ean] - GetReserved(bin.Id, ean);
                        if (available <= 0) continue;
                        int take = Math.Min(available, remaining);
                        picks.Add(new PickDto { Ean = ean, BinId = bin.Id, Qty = take });
                        AddReserved(bin.Id, ean, take);
                        remaining -= take;
                    }
                }

                foreach (var (gridId, eanMap) in _binsByGridAndEan)
                {
                    if (remaining <= 0) break;
                    if (gridId == packingGridId) continue;
                    if (!eanMap.TryGetValue(ean, out var otherBins)) continue;
                    foreach (var bin in otherBins)
                    {
                        if (remaining <= 0) break;
                        int available = bin.Stock[ean] - GetReserved(bin.Id, ean);
                        if (available <= 0) continue;
                        int take = Math.Min(available, remaining);
                        picks.Add(new PickDto { Ean = ean, BinId = bin.Id, Qty = take });
                        AddReserved(bin.Id, ean, take);
                        remaining -= take;
                    }
                }

                if (remaining > 0) return null;
            }
            return picks;
        }

        private List<PickDto>? BuildPicksVirtual(Shipment shipment, string packingGridId)
        {
            var picks = new List<PickDto>();
            foreach (var (ean, qty) in shipment.Items)
            {
                int remaining = qty;

                if (_binsHashset.TryGetValue(packingGridId, out var localEans) &&
                    localEans.TryGetValue(ean, out var localBins))
                {
                    foreach (var bin in localBins)
                    {
                        if (remaining <= 0) break;
                        int available = bin.Stock[ean] - GetReserved(bin.Id, ean);
                        if (available <= 0) continue;
                        int take = Math.Min(available, remaining);
                        picks.Add(new PickDto { Ean = ean, BinId = bin.Id, Qty = take });
                        AddReserved(bin.Id, ean, take);
                        remaining -= take;
                    }
                }

                foreach (var (gridId, eanMap) in _binsHashset)
                {
                    if (remaining <= 0) break;
                    if (gridId == packingGridId) continue;
                    if (!eanMap.TryGetValue(ean, out var otherBins)) continue;
                    foreach (var bin in otherBins)
                    {
                        if (remaining <= 0) break;
                        int available = bin.Stock[ean] - GetReserved(bin.Id, ean);
                        if (available <= 0) continue;
                        int take = Math.Min(available, remaining);
                        picks.Add(new PickDto { Ean = ean, BinId = bin.Id, Qty = take });
                        AddReserved(bin.Id, ean, take);
                        remaining -= take;
                    }
                }

                if (remaining > 0) return null;
            }
            return picks;
        }
        private int GetReserved(string binId, string ean)
        {
            if (_reservedStock.TryGetValue(binId, out var reserved) &&
                reserved.TryGetValue(ean, out int qty))
                return qty;
            return 0;
        }

        private void AddReserved(string binId, string ean, int qty)
        {
            if (!_reservedStock.ContainsKey(binId))
                _reservedStock[binId] = new();
            _reservedStock[binId][ean] = GetReserved(binId, ean) + qty;
        }

        private static string NormalizeSortingDirection(string dir) =>
            dir.ToLowerInvariant().Replace("-", "").Replace(" ", "").Replace("_", "");
    }
}
