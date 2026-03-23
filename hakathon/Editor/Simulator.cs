using AdvancedRouter;
using System.Diagnostics;
using System.Text.Json;

namespace hakathon.Editor
{
    public enum SimEventType
    {
        ShipmentReceived,
        ShipmentRouterTriggered,
        BinRequestedAtPort,
        BinArrivedAtPort,
        BinPickCompleted,
        BinTransferStarted,
        BinTransferCompleted,
        TruckArrived,
        PortBreakStarted,
        PortBreakEnded,
        ShiftStarted,
        ShiftEnded
    }

    public class SimEvent : IComparable<SimEvent>
    {
        public double SimTime { get; set; }        // seconds from simulation start
        public SimEventType Type { get; set; }
        public object? Data { get; set; }
        private static int _sequence = 0;
        private int _order = Interlocked.Increment(ref _sequence); // FIFO tiebreak

        public int CompareTo(SimEvent? other)
        {
            if (other == null) return -1;
            int timeCompare = SimTime.CompareTo(other.SimTime);
            return timeCompare != 0 ? timeCompare : _order.CompareTo(other._order);
        }
    }
    public class EventQueue
    {
        private readonly PriorityQueue<SimEvent, SimEvent> _queue = new(Comparer<SimEvent>.Create((a, b) => a.CompareTo(b)));

        public int Count => _queue.Count;

        public void Enqueue(SimEvent e) => _queue.Enqueue(e, e);

        public SimEvent Dequeue() => _queue.Dequeue();

        public bool TryDequeue(out SimEvent e) => _queue.TryDequeue(out e, out _);

        public void Schedule(SimEventType type, double simTime, object? data = null)
        {
            _queue.Enqueue(new SimEvent
            {
                Type = type,
                SimTime = simTime,
                Data = data
            }, new SimEvent { SimTime = simTime });
        }
    }
    public record ShipmentReceivedData(Shipment Shipment);
    public record BinRequestedData(Bin Bin, Port Port, Shipment Shipment, double EstimatedDeliveryTime);
    public record BinArrivedData(Bin Bin, Port Port, Shipment Shipment);
    public record BinPickCompletedData(Bin Bin, Port Port, Shipment Shipment, double PickDuration);
    public record BinTransferData(Bin Bin, string SourceGridId, string DestGridId, double TransferDuration);
    public record TruckArrivedData(string SortingDirection);
    public record PortBreakData(Port Port, BreakScheduleDto Break);
    public record ShiftData(Grid Grid, ShiftDto Shift);

    public class Simulator
    {
        // events
        private readonly EventQueue _events = new();
        // data
        private readonly ParametersDto _parameters;
        private readonly List<Shipment> _shipments;
        private readonly List<Bin> _bins;
        private readonly List<Grid> _grids;
        private readonly List<TruckScheduleDto> _truckSchedules;
        private readonly Random _rng = new();
        private double _now = 0;
        // router
        private Process? _routerProcess;
        private string _routerCommand = string.Empty;
        private bool isVirtualized = false;
        // logging
        private StreamWriter? _logWriter;
        private static readonly JsonSerializerOptions _loggingOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        // stale shipment tracking - DEPRECATED
        private int _lastBacklogCount = -1;
        private int _staleTriggers = 0;
        private Dictionary<string, double> _shipmentBacklogEntryTime = new();
        // indexing
            // bins
        private Dictionary<string, Bin> _binById = new();
        private Dictionary<string, List<Bin>> _binsByGrid = new();
        //private Dictionary<string, Dictionary<string, List<Bin>>> _binsByGridAndEan = new();
        private Dictionary<string, Dictionary<string, HashSet<Bin>>> _binsByGridAndEan = new();
            // shipments
        private Dictionary<string, Shipment> _shipmentById = new();
        private HashSet<Bin> _availableBins = new();
        private CAdvancedRouter _advancedRouter = null;


        public Simulator(List<Shipment> shipments, List<Bin> bins, List<Grid> grids, List<TruckScheduleDto> truckSchedules, ParametersDto parameters, bool virtRouter)
        {
            _shipments = shipments;
            _shipmentById = shipments.ToDictionary(s => s.Id);
            
            _bins = bins;
            _availableBins = _bins.Where(b => b.Status == BinStatus.Available).ToHashSet();
            _binById = bins.ToDictionary(b => b.Id);
            _binsByGrid = bins.GroupBy(b => b.GridId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var bin in _bins)
            {
                if (!_binsByGridAndEan.TryGetValue(bin.GridId, out var eanMap))
                {
                    eanMap = new Dictionary<string, HashSet<Bin>>();
                    _binsByGridAndEan[bin.GridId] = eanMap;
                }
                foreach (var ean in bin.Stock.Keys)
                {
                    if (!eanMap.TryGetValue(ean, out var list))
                    {
                        list = new HashSet<Bin>();
                        eanMap[ean] = list;
                    }
                    list.Add(bin);
                }
            }

            _grids = grids;
            _parameters = parameters;
            _truckSchedules = truckSchedules;

            isVirtualized = virtRouter;

            _advancedRouter = new CAdvancedRouter(_grids, _truckSchedules);
        }

        public void Run()
        {
            double simDuration = (_parameters.SimulationEndTime - _parameters.SimulationStartTime).TotalSeconds;

            foreach (var grid in _grids)
            {
                foreach (var shift in grid.Shifts)
                {
                    for (int day = 0; day * 86400 < simDuration + 86400; day++)
                    {
                        DateTime dayStart = _parameters.SimulationStartTime.Date.AddDays(day);

                        DateTime shiftStart = Helpers.CombineDateAndTimeForSim(dayStart, shift.ShiftStart);
                        DateTime shiftEnd = Helpers.CombineDateAndTimeForSim(dayStart, shift.ShiftEnd);

                        // handle midnight crossover
                        if (shiftEnd <= shiftStart)
                            shiftEnd = shiftEnd.AddDays(1);

                        double shiftStartSim = ToSimSeconds(shiftStart);
                        double shiftEndSim = ToSimSeconds(shiftEnd);

                        if (shiftStartSim < 0) shiftStartSim = 0; // clamp if shift started before simulation
                        if (shiftStartSim < simDuration)
                            _events.Schedule(SimEventType.ShiftStarted, shiftStartSim, new ShiftData(grid, shift));

                        if (shiftEndSim >= 0 && shiftEndSim < simDuration)
                            _events.Schedule(SimEventType.ShiftEnded, shiftEndSim, new ShiftData(grid, shift));

                        foreach (var breakSchedule in shift.Breaks ?? new())
                        {
                            DateTime breakStart = Helpers.CombineDateAndTimeForSim(dayStart, breakSchedule.BreakStart);
                            DateTime breakEnd = Helpers.CombineDateAndTimeForSim(dayStart, breakSchedule.BreakEnd);

                            if (breakEnd <= breakStart)
                                breakEnd = breakEnd.AddDays(1);

                            double breakStartSim = ToSimSeconds(breakStart);
                            double breakEndSim = ToSimSeconds(breakEnd);

                            foreach (var portConfig in shift.ShiftPortConfig)
                            {
                                var port = grid.Ports.FirstOrDefault(p => p.Id == portConfig.PortId);
                                if (port == null) continue;
                                if (breakStartSim >= 0 && breakStartSim < simDuration)
                                    _events.Schedule(SimEventType.PortBreakStarted, breakStartSim,
                                        new PortBreakData(port, breakSchedule));
                                if (breakEndSim >= 0 && breakEndSim < simDuration)
                                    _events.Schedule(SimEventType.PortBreakEnded, breakEndSim,
                                        new PortBreakData(port, breakSchedule));
                            }
                        }
                    }
                }
            }

            foreach (var schedule in _truckSchedules)
            {
                for (int day = 0; day * 86400 < simDuration + 86400; day++)
                {
                    DateTime dayStart = _parameters.SimulationStartTime.Date.AddDays(day);
                    string dayName = dayStart.DayOfWeek.ToString();

                    if (!schedule.Weekdays.Contains(dayName)) continue;

                    foreach (var pullTime in schedule.PullTimes)
                    {
                        DateTime truckTime = Helpers.CombineDateAndTimeForSim(dayStart, pullTime);
                        double truckSim = ToSimSeconds(truckTime);

                        if (truckSim >= 0 && truckSim < simDuration)
                            _events.Schedule(SimEventType.TruckArrived, truckSim,
                                new TruckArrivedData(schedule.SortingDirection));
                    }
                }
            }

            // seed initial events
            foreach (var shipment in _shipments)
            {
                double offset = (shipment.CreatedAt - _parameters.SimulationStartTime).TotalSeconds;
                if (offset < 0) offset = 0; // clamp to start if shipment predates simulation
                _events.Schedule(SimEventType.ShipmentReceived, offset, new ShipmentReceivedData(shipment));
            }

            // first router trigger
            _events.Schedule(SimEventType.ShipmentRouterTriggered, 0);

            int eventCount = 0;
            while (_events.TryDequeue(out SimEvent e))
            {
                var simStepTime = Stopwatch.GetTimestamp();
                if (e.SimTime > (_parameters.SimulationEndTime - _parameters.SimulationStartTime).TotalSeconds)
                    break;
                _now = e.SimTime;
                ProcessEvent(e);

                if (++eventCount % 10000 == 0)
                {
                    _logWriter.Flush();
                }
                var simStepElapsed = Stopwatch.GetElapsedTime(simStepTime);
                //Console.WriteLine($"step time: {simStepElapsed.TotalMilliseconds:F0}ms");
            }

            LogFinalMetrics();
            _logWriter.Flush();
        }

        #region router interaction
        public void InitRouter(string routerCommand)
        {
            _routerCommand = routerCommand;
        }
        
        private string? CallRouter(string json)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _routerCommand,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            process.StandardInput.WriteLine(json);
            process.StandardInput.Close();

            string response = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.Dispose();

            if (!string.IsNullOrWhiteSpace(errors))
                Console.Error.WriteLine($"Router stderr: {errors}");

            return string.IsNullOrWhiteSpace(response) ? null : response;
        }
        #endregion

        private void ProcessEvent(SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.ShipmentReceived:
                    OnShipmentReceived((ShipmentReceivedData)e.Data!);
                    break;
                case SimEventType.ShipmentRouterTriggered:
                    OnRouterTriggered();
                    break;
                case SimEventType.BinArrivedAtPort:
                    OnBinArrived((BinArrivedData)e.Data!);
                    break;
                case SimEventType.BinPickCompleted:
                    OnBinPickCompleted((BinPickCompletedData)e.Data!);
                    break;
                case SimEventType.TruckArrived:
                    OnTruckArrived((TruckArrivedData)e.Data!);
                    break;
                case SimEventType.BinTransferCompleted:
                    OnBinTransferCompleted((BinTransferData)e.Data!);
                    break;
                case SimEventType.PortBreakStarted:
                    OnPortBreakStarted((PortBreakData)e.Data!);
                    break;
                case SimEventType.PortBreakEnded:
                    OnPortBreakEnded((PortBreakData)e.Data!);
                    break;
                case SimEventType.ShiftStarted:
                    OnShiftStarted((ShiftData)e.Data!);
                    break;
                case SimEventType.ShiftEnded:
                    OnShiftEnded((ShiftData)e.Data!);
                    break;
            }
        }

        #region events
        private void OnTruckArrived(TruckArrivedData data)
        {
            Console.Error.WriteLine($"[{_now}s] Truck arrived for {data.SortingDirection}");

            var normalizedTruckDir = NormalizeSortingDirection(data.SortingDirection);
            var toShip = _shipments
                .Where(s => s.Status == ShipmentStatus.Packed &&
                            NormalizeSortingDirection(s.SortingDirection) == normalizedTruckDir)
                .ToList();

            Console.Error.WriteLine($"  Shipping {toShip.Count} shipments");

            LogEvent("TruckArrived", _now, new { sorting_direction = data.SortingDirection });

            foreach (var shipment in toShip)
            {
                shipment.Status = ShipmentStatus.Shipped;
                double leadTime = (_parameters.SimulationStartTime.AddSeconds(_now) - shipment.CreatedAt).TotalHours;
                double dwellTime = (_parameters.SimulationStartTime.AddSeconds(_now) - shipment.CompletionTime!.Value).TotalHours;
                bool packedOnTime = (shipment.CompletionTime!.Value - shipment.CreatedAt).TotalHours <= 24;

                LogEvent("ShipmentShipped", _now, new
                {
                    shipment_id = shipment.Id,
                    sorting_direction = data.SortingDirection,
                    lead_time_hours = Math.Round(leadTime, 2),
                    dwell_time_hours = Math.Round(dwellTime, 2),
                    packed_on_time = packedOnTime
                });
                shipment.ShippedTime = _parameters.SimulationStartTime.AddSeconds(_now);
            }
        }

        private void OnShipmentReceived(ShipmentReceivedData data)
        {
            if (data.Shipment.Status != ShipmentStatus.Received)
                return; // already processed, don't reset

            LogEvent("ShipmentReceived", _now, new
            {
                shipment_id = data.Shipment.Id,
                items = data.Shipment.Items,
                handling_flags = data.Shipment.HandlingFlags,
                sorting_direction = data.Shipment.SortingDirection
            });
        }

        private void OnRouterTriggered()
        {
            var rollbackStart = Stopwatch.GetTimestamp();
            RollbackUnstartedShipments();
            Console.Error.WriteLine($"Rollback took: {Stopwatch.GetElapsedTime(rollbackStart).TotalMilliseconds:F0}ms"); 
            
            var backlog = _shipments
                .Where(s => s.Status == ShipmentStatus.Received
                    && (s.CreatedAt - _parameters.SimulationStartTime).TotalSeconds <= _now)
                .OrderByDescending(s => s.HandlingFlags?.Contains("priority") ?? false)
                .ThenBy(s => s.CreatedAt)
                .ToList();
            
            if (backlog.Count == 0)
            {
                Console.Error.WriteLine("No backlog, skipping router call");
                // only reschedule if simulation hasn't ended
                if (_now < (_parameters.SimulationEndTime - _parameters.SimulationStartTime).TotalSeconds)
                    _events.Schedule(SimEventType.ShipmentRouterTriggered, _now + _parameters.RouterIntervalSeconds);
                return;
            }

            foreach (var shipment in backlog)
            {
                if (!_shipmentBacklogEntryTime.ContainsKey(shipment.Id))
                    _shipmentBacklogEntryTime[shipment.Id] = _now;
            }

            // find shipments that have been in backlog too long
            var stalled = backlog.Where(s => _now - _shipmentBacklogEntryTime[s.Id] > 172800).ToList(); // 48 hours

            if (stalled.Any())
            {
                var noStock = stalled.Count(s => !s.Items.All(item =>
                    _bins.Any(b => b.Stock.ContainsKey(item.Key))));
                var noPort = stalled.Count - noStock;
                Console.Error.WriteLine($"Stalled breakdown: {noStock} no stock, {noPort} no compatible port");
                Console.Error.WriteLine($"going to remove: {stalled.Count} items for being stalled for too long");
            }

            foreach (var s in stalled)
            {
                //Console.Error.WriteLine($"[{_now}s] Shipment {s.Id} unroutable after 48 hours — marking as failed");
                bool hasStock = s.Items.All(item =>
                    _bins.Any(b => b.Stock.TryGetValue(item.Key, out int qty) && qty >= item.Value));
                //Console.Error.WriteLine($"Stalled {s.Id} flags=[{string.Join(",", s.HandlingFlags)}] hasStock={hasStock}");
                
                LogEvent("ShipmentUnroutable", _now, new
                {
                    shipment_id = s.Id,
                    handling_flags = s.HandlingFlags,
                    reason = "No compatible port or items in bins found after 48 hours"
                });
                s.Status = ShipmentStatus.Failed;
                _shipmentBacklogEntryTime.Remove(s.Id);
            }

            // remove stalled from backlog before sending to router
            backlog = backlog.Except(stalled).ToList();
            if (backlog.Count == 0)
            {
                _events.Schedule(SimEventType.ShipmentRouterTriggered, _now + _parameters.RouterIntervalSeconds);
                return;
            }

            RouterResponse assignments = null;
            /// ---
            if (!isVirtualized)
                RouteSerialized(backlog, out assignments);
            else
                RouteVirtualized(backlog, out assignments);
            if (assignments == null) return;

            //var assignemntPerformance = Stopwatch.GetTimestamp();
            //double totalShipmentTime = 0;
            //double totalConsolidationTime = 0;
            //double binTransferTIme = 0;
            //double ShipmentPortTime = 0;
            //
            //double binStatusChange = 0;
            //double binTransfer = 0;
            foreach (var assignment in assignments.Assignments)
            {
                _shipmentById.TryGetValue(assignment.ShipmentId, out var shipment);
                if (shipment == null) continue;

                var shipmentPick = Stopwatch.GetTimestamp();
                shipment.Status = ShipmentStatus.Routed;                
                shipment.PackingGridId = assignment.PackingGrid;
                shipment.Picks = assignment.Picks.Select(p => new PickItem
                {
                    Ean = p.Ean,
                    BinId = p.BinId,
                    Quantity = p.Qty
                }).ToList();

                // reserve bins
                foreach (var pick in shipment.Picks)
                {
                    if (!_binById.TryGetValue(pick.BinId, out var bin)) continue;
                    bin.ReservedQuantities[pick.Ean] = bin.ReservedQuantities.GetValueOrDefault(pick.Ean) + pick.Quantity;
                    _reservedBinIds.Add(pick.BinId);
                }

                //totalShipmentTime += Stopwatch.GetElapsedTime(shipmentPick).TotalMilliseconds;


                var consolidationtime = Stopwatch.GetTimestamp();
                // check if all bins are in the same grid already
                bool needsConsolidation = shipment.Picks
                    .Select(p => _binById.TryGetValue(p.BinId, out var b) ? b.GridId : null)
                    .Distinct()
                    .Count() > 1;

                if (needsConsolidation)
                {
                    var time1 = Stopwatch.GetTimestamp();
                    shipment.Status = ShipmentStatus.Consolidation;
                    ScheduleBinTransfers(shipment/*, ref binStatusChange, ref binTransfer*/);
                    //binTransferTIme += Stopwatch.GetElapsedTime(time1).TotalMilliseconds;
                }
                else
                {
                    var time2 = Stopwatch.GetTimestamp();
                    shipment.Status = ShipmentStatus.Ready;
                    AssignShipmentToPort(shipment);
                    //ShipmentPortTime += Stopwatch.GetElapsedTime(time2).TotalMilliseconds;
                }
                //totalConsolidationTime += Stopwatch.GetElapsedTime(consolidationtime).TotalMilliseconds;
            }
            //Console.Error.WriteLine($"      pick time: {(int)totalShipmentTime}ms");
            //Console.Error.WriteLine($"      consolidation time {(int)totalConsolidationTime}ms");
            //Console.Error.WriteLine($"              bin transfer time {(int)binTransferTIme}ms");
            //Console.Error.WriteLine($"              shipment to port time {(int)ShipmentPortTime}ms");
            //Console.Error.WriteLine($"                  bin status change {(int)binStatusChange}ms");
            //Console.Error.WriteLine($"                  bin trasnfer time {(int)binTransfer}ms");
            //Console.Error.WriteLine($"Total assignemnt time: {Stopwatch.GetElapsedTime(assignemntPerformance).TotalMilliseconds:F0}ms");

            //Console.Error.WriteLine($"After routing - Received:{_shipments.Count(s => s.Status == ShipmentStatus.Received)} Routed:{_shipments.Count(s => s.Status == ShipmentStatus.Routed)} Ready:{_shipments.Count(s => s.Status == ShipmentStatus.Ready)} Consolidation:{_shipments.Count(s => s.Status == ShipmentStatus.Consolidation)} Picking:{_shipments.Count(s => s.Status == ShipmentStatus.Picking)}");

            // reschedule next router run
            int total = _shipments.Count;
            int done = _shipments.Count(s => s.Status == ShipmentStatus.Shipped ||
                                              s.Status == ShipmentStatus.Packed ||
                                              s.Status == ShipmentStatus.Failed);
            double pct = (double)done / total * 100;
            Console.Error.WriteLine($"Progress: {done}/{total} ({pct:F1}%)");

            _events.Schedule(SimEventType.ShipmentRouterTriggered, _now + _parameters.RouterIntervalSeconds);
        }

        private void RouteVirtualized(List<Shipment> backlog, out RouterResponse assignments)
        {
            var virtRouter = Stopwatch.GetTimestamp();

            var availableBins = _availableBins.ToList();
            _advancedRouter.Update(_parameters.SimulationStartTime.AddSeconds(_now), backlog, _bins, _binsByGridAndEan);
            //var router = new CAdvancedRouter(_parameters.SimulationStartTime.AddSeconds(_now), backlog, availableBins, _grids, _truckSchedules);
            assignments = _advancedRouter.Route();
            var virtRouterElapsed = Stopwatch.GetElapsedTime(virtRouter);
            Console.WriteLine($"virtualised router cycle time taken:{virtRouterElapsed.TotalMilliseconds:F0}ms");
        }

        private void RouteSerialized(List<Shipment> backlog, out RouterResponse assignments)
        {
            var jsonSerStart = Stopwatch.GetTimestamp();
            var routerInput = new
            {
                state = new
                {
                    now = _parameters.SimulationStartTime.AddSeconds(_now).ToString("o"),
                    shipments_backlog = backlog.Select(s => new
                    {
                        id = s.Id,
                        created_at = s.CreatedAt.ToString("o"),
                        items = s.Items,
                        handling_flags = s.HandlingFlags,
                        sorting_direction = s.SortingDirection
                    }),
                    stock_bins = _bins.Select(b => new
                    {
                        bin_id = b.Id,
                        grid_id = b.GridId,
                        items = b.Stock
                    }),
                    grids = _grids.Select(g => new
                    {
                        id = g.Id,
                        shifts = g.Shifts.Select(shift =>
                        {
                            DateTime currentDate = _parameters.SimulationStartTime.AddSeconds(_now).Date;
                            var startAt = Helpers.CombineDateAndTimeForSim(currentDate, shift.ShiftStart);
                            var endAt = Helpers.CombineDateAndTimeForSim(currentDate, shift.ShiftEnd);
                            if (endAt <= startAt) endAt = endAt.AddDays(1);
                            return new
                            {
                                start_at = startAt.ToString("o"),
                                end_at = endAt.ToString("o"),
                                port_config = shift.ShiftPortConfig.Select(p => new
                                {
                                    port_id = p.PortId ?? $"{g.Id}-{p.PortIndex ?? "0"}",
                                    handling_flags = p.HandlingFlags
                                })
                            };
                        })
                    }),
                    truck_arrival_schedules = _truckSchedules.Count == 0
                        ? (object)new { schedules = Array.Empty<object>() }
                        : (object)new
                        {
                            schedules = _truckSchedules.Select(t => new
                            {
                                sorting_direction = t.SortingDirection,
                                pull_times = t.PullTimes,
                                weekdays = t.Weekdays
                            })
                        }
                }
            };

            string json = JsonSerializer.Serialize(routerInput);
            var jsonSerEnd = Stopwatch.GetElapsedTime(jsonSerStart);
            Console.Error.WriteLine($"serialization took: {jsonSerEnd.TotalMilliseconds:F0}ms");

            // read router response from stdin
            var routerStart = Stopwatch.GetTimestamp();
            var response = CallRouter(json);
            var routerElapsed = Stopwatch.GetElapsedTime(routerStart);
            Console.Error.WriteLine($"Router took: {routerElapsed.TotalMilliseconds:F0}ms");

            assignments = null;

            if (string.IsNullOrWhiteSpace(response))
            {
                Console.Error.WriteLine("Empty router response, skipping.");
                _events.Schedule(SimEventType.ShipmentRouterTriggered, _now + _parameters.RouterIntervalSeconds);
                return;
            }
            if (response == null) return;

            assignments = JsonSerializer.Deserialize<RouterResponse>(response);
            Console.Error.WriteLine($"Backlog count: {backlog.Count}, assignments: {assignments?.Assignments.Count ?? 0}");
        }

        private void RollbackUnstartedShipments()
        {
            var toRollback = _shipments
                .Where(s => s.Status == ShipmentStatus.Routed).ToList();

            foreach (var shipment in toRollback)
            {
                // clear bin reservations
                foreach (var pick in shipment.Picks)
                    _reservedBinIds.Remove(pick.BinId);
                
                shipment.Picks.Clear();
                shipment.PackingGridId = string.Empty;
                shipment.Status = ShipmentStatus.Received;
            }

            if (toRollback.Any())
                Console.Error.WriteLine($"Rolled back {toRollback.Count} shipments to Received");
        }

        private void SetBinStatus(Bin bin, BinStatus newStatus)
        {
            BinStatus oldStatus = bin.Status;
            bin.Status = newStatus;

            // remove from index if was available
            if (oldStatus == BinStatus.Available)
            {
                if (_binsByGridAndEan.TryGetValue(bin.GridId, out var eanMap))
                    foreach (var ean in bin.Stock.Keys)
                        if (eanMap.TryGetValue(ean, out var set))
                            set.Remove(bin);
            }

            // add to index if now available
            if (newStatus == BinStatus.Available)
            {
                if (!_binsByGridAndEan.TryGetValue(bin.GridId, out var eanMap))
                {
                    eanMap = new Dictionary<string, HashSet<Bin>>();
                    _binsByGridAndEan[bin.GridId] = eanMap;
                }
                foreach (var ean in bin.Stock.Keys)
                {
                    if (!eanMap.TryGetValue(ean, out var list))
                    {
                        list = new HashSet<Bin>();
                        eanMap[ean] = list;
                    }
                    list.Add(bin);
                }
            }
        }

        // bins
        private void OnBinArrived(BinArrivedData data)
        {
            LogEvent("BinArrivedAtPort", _now, new
            {
                bin_id = data.Bin.Id,
                port_id = data.Port.Id,
                shipment_id = data.Shipment.Id
            });

            var nextPick = data.Shipment.Picks.FirstOrDefault(p => p.BinId == data.Bin.Id && !p.Completed);
            if (nextPick == null) return;

            double pickDuration = PickDuration(data.Shipment, nextPick.Quantity);

            _events.Schedule(SimEventType.BinPickCompleted, _now + pickDuration,
                new BinPickCompletedData(data.Bin, data.Port, data.Shipment, pickDuration));
        }

        private void OnBinPickCompleted(BinPickCompletedData data)
        {
            // mark this pick as done
            var pick = data.Shipment.Picks.FirstOrDefault(p => p.BinId == data.Bin.Id && !p.Completed);
            if (pick != null) pick.Completed = true;

            LogEvent("BinPickCompleted", _now, new
            {
                bin_id = data.Bin.Id,
                port_id = data.Port.Id,
                shipment_id = data.Shipment.Id,
                pick_duration = data.PickDuration
            });

            // release bin and notify waiting ports
            SetBinStatus(data.Bin, BinStatus.Available);
            data.Bin.ReservedForPortId = null;
            _reservedBinIds.Remove(data.Bin.Id);
            LogEvent("BinReleased", _now, new
            {
                bin_id = data.Bin.Id,
                port_id = data.Port.Id
            });

            if (data.Bin.WaitingPorts.TryDequeue(out string? waitingPortId))
            {
                var waitingPort = _grids.SelectMany(g => g.Ports)
                    .FirstOrDefault(p => p.Id == waitingPortId);
                if (waitingPort?.CurrentShipment != null)
                {
                    //data.Bin.Status = BinStatus.Reserved;
                    SetBinStatus(data.Bin, BinStatus.Reserved);
                    data.Bin.ReservedForPortId = waitingPortId;
                    double deliveryTime = BinDeliveryTime(_grids.First(g => g.Id == waitingPort.GridId));
                    _events.Schedule(SimEventType.BinArrivedAtPort, _now + deliveryTime,
                        new BinArrivedData(data.Bin, waitingPort, waitingPort.CurrentShipment));
                }
            }

            bool allPicked = data.Shipment.Picks.All(p => p.Completed);
            if (allPicked)
            {
                data.Shipment.Status = ShipmentStatus.Packed;
                data.Shipment.CompletionTime = _parameters.SimulationStartTime.AddSeconds(_now);
                data.Port.ShipmentQueue.RemoveFirst();

                LogEvent("ShipmentPacked", _now, new
                {
                    shipment_id = data.Shipment.Id,
                    port_id = data.Port.Id,
                    packing_duration = data.PickDuration
                });

                // drain grid queue into available ports
                var grid = _grids.First(g => g.Id == data.Port.GridId);
                while (grid.GridQueue.Count > 0)
                {
                    // find any idle port that can accept the next shipment
                    var next = grid.GridQueue.First!.Value;
                    var idlePort = grid.Ports
                        .Where(p => p.Status == PortStatus.Idle && p.CanAccept(next))
                        .OrderBy(p => p.ShipmentQueue.Count)
                        .FirstOrDefault();

                    if (idlePort == null) break;

                    grid.GridQueue.RemoveFirst();
                    LogGridQueueUpdate(grid, next, "removed");
                    if (next.IsPriority)
                        idlePort.ShipmentQueue.AddFirst(next);
                    else
                        idlePort.ShipmentQueue.AddLast(next);
                    next.Status = ShipmentStatus.Picking;

                    StartNextPick(idlePort);
                }

                if (data.Port.Status == PortStatus.PendingClose)
                {
                    ClosePort(data.Port);
                    return; // don't start next pick
                }
                StartNextPick(data.Port);
            }
            else
            {
                StartNextPick(data.Port);
            }
        }

        private void OnBinTransferCompleted(BinTransferData data)
        {
            string oldGridId = data.SourceGridId;

            // remove from old grid index
            if (_binsByGridAndEan.TryGetValue(oldGridId, out var oldEanMap))
                foreach (var ean in data.Bin.Stock.Keys)
                    if (oldEanMap.TryGetValue(ean, out var list))
                        list.Remove(data.Bin); 
            
            data.Bin.GridId = data.DestGridId;
            //data.Bin.Status = BinStatus.Available;
            SetBinStatus(data.Bin, BinStatus.Available);

            LogEvent("BinTransferCompleted", _now, new
            {
                bin_id = data.Bin.Id,
                source_grid = data.SourceGridId,
                dest_grid = data.DestGridId
            });

            // check if all bins for any consolidating shipment are now in the same grid
            var readyShipments = _shipments
                .Where(s => s.Status == ShipmentStatus.Consolidation)
                .Where(s => s.Picks.All(p => _binById.TryGetValue(p.BinId, out var b) && b.GridId == s.PackingGridId))
                .ToList();

            foreach (var shipment in readyShipments)
            {
                shipment.Status = ShipmentStatus.Ready;
                LogEvent("ShipmentReady", _now, new
                {
                    shipment_id = shipment.Id,
                    packing_grid = shipment.PackingGridId
                });
                AssignShipmentToPort(shipment);
            }
        }
        
        // breaks
        private void OnPortBreakStarted(PortBreakData data)
        {
            var port = data.Port;
            Console.Error.WriteLine($"[{_now}s] Break started on {port.Id}, status={port.Status}");

            if (port.Status == PortStatus.Busy)
            {
                // finish current shipment then close
                SetPortStatus(port, PortStatus.PendingClose);
                port.CurrentBreak = data.Break;
                LogEvent("PortPendingClose", _now, new { port_id = port.Id });
            }
            else
            {
                // idle or already closed — close immediately and roll back queue
                ClosePort(port);
            }
        }

        private void OnPortBreakEnded(PortBreakData data)
        {
            var port = data.Port;
            Console.Error.WriteLine($"[{_now}s] Break ended on {port.Id}");

            SetPortStatus(port, PortStatus.Idle);
            port.CurrentBreak = null;
            LogEvent("PortBreakEnded", _now, new { port_id = port.Id });

            // drain grid queue
            var grid = _grids.First(g => g.Id == port.GridId);
            while (grid.GridQueue.Count > 0)
            {
                var next = grid.GridQueue.First!.Value;
                if (!port.CanAccept(next)) break;
                
                grid.GridQueue.RemoveFirst();
                //LogGridQueueUpdate(grid, next, "removed");

                if (next.IsPriority)
                    port.ShipmentQueue.AddFirst(next);
                else
                    port.ShipmentQueue.AddLast(next);
                next.Status = ShipmentStatus.Picking;
            }

            if (port.ShipmentQueue.Count > 0)
                StartNextPick(port);
        }

        // shifts
        private void OnShiftStarted(ShiftData data)
        {            
            Console.Error.WriteLine($"[{_now}s] Shift started on grid {data.Grid.Id}");
            LogEvent("ShiftStarted", _now, new { grid_id = data.Grid.Id });

            foreach (var portConfig in data.Shift.ShiftPortConfig)
            {
                var port = data.Grid.Ports.FirstOrDefault(p =>
                    p.Id == portConfig.PortId ||
                    p.Id == portConfig.PortIndex ||
                    p.Id == $"{data.Grid.Id}-{portConfig.PortId}" ||
                    p.Id == $"{data.Grid.Id}-{portConfig.PortIndex}");

                if (port == null) continue;
                if (port.Status == PortStatus.Closed || _now == 0)
                {
                    SetPortStatus(port, PortStatus.Idle);
                    LogEvent("PortOpened", _now, new { port_id = port.Id });
                }
            }
            //Console.Error.WriteLine($"After shift start {data.Grid.Id}: {data.Grid.Ports.Count(p => p.Status == PortStatus.Idle)} idle ports");

            // drain grid queue into newly opened ports
            while (data.Grid.GridQueue.Count > 0)
            {
                var next = data.Grid.GridQueue.First!.Value;
                var port = data.Grid.Ports
                    .Where(p => p.Status == PortStatus.Idle && p.CanAccept(next))
                    .OrderByDescending(p => p.HandlingFlags.Count)
                    .FirstOrDefault();

                if (port == null) break;

                data.Grid.GridQueue.RemoveFirst();
                //LogGridQueueUpdate(data.Grid, next, "removed");

                if (next.IsPriority)
                    port.ShipmentQueue.AddFirst(next);
                else
                    port.ShipmentQueue.AddLast(next);
                next.Status = ShipmentStatus.Picking;
                StartNextPick(port);
            }
        }

        private void OnShiftEnded(ShiftData data)
        {
            Console.Error.WriteLine($"[{_now}s] Shift ended on grid {data.Grid.Id}");
            LogEvent("ShiftEnded", _now, new { grid_id = data.Grid.Id });

            foreach (var port in data.Grid.Ports)
            {
                if (port.Status == PortStatus.Busy)
                {
                    SetPortStatus(port, PortStatus.PendingClose);
                    port.CurrentBreak = null;
                    LogEvent("PortPendingClose", _now, new { port_id = port.Id });
                }
                else if (port.Status == PortStatus.Idle)
                {
                    ClosePort(port);
                }
            }
        }
        #endregion

        #region event helpers
        // ports
        private void ClosePort(Port port)
        {
            SetPortStatus(port, PortStatus.Closed);
            LogEvent("PortClosed", _now, new { port_id = port.Id });

            var grid = _grids.First(g => g.Id == port.GridId);
            foreach (var shipment in port.ShipmentQueue)
            {
                // keep as Ready/Picking, just move back to grid queue
                shipment.Status = ShipmentStatus.Ready;
                if (shipment.IsPriority)
                {
                    grid.GridQueue.AddFirst(shipment);
                    LogGridQueueUpdate(grid, shipment, "addedFirst");
                }
                else
                {
                    grid.GridQueue.AddLast(shipment);
                    LogGridQueueUpdate(grid, shipment, "addedLast");
                }
            }
            port.ShipmentQueue.Clear();
            port.CurrentShipment = null;
        }

        private void SetPortStatus(Port port, PortStatus newStatus)
        {
            if (port.Status == newStatus) return;
            var oldStatus = port.Status;
            port.Status = newStatus;
            LogEvent("PortStatusChanged", _now, new
            {
                port_id = port.Id,
                old_status = oldStatus.ToString(),
                new_status = newStatus.ToString()
            });
        }

       
        private void AssignShipmentToPort(Shipment shipment)
        {
            var grid = _grids.FirstOrDefault(g => g.Id == shipment.PackingGridId);
            if (grid == null)
            {
                Console.Error.WriteLine($"Grid {shipment.PackingGridId} not found for {shipment.Id}");
                return;
            }

            int minQueueSize = grid.Ports
                    .Where(p => p.Status != PortStatus.Closed && p.CanAccept(shipment))
                    .Min(p => (int?)p.ShipmentQueue.Count) ?? -1;

            if (minQueueSize == -1)
            {
                //Console.Error.WriteLine($"No port for {shipment.Id} in {grid.Id}. Ports: {grid.Ports.Count}, statuses: {string.Join(",", grid.Ports.Select(p => p.Status))}");
                if (shipment.IsPriority)
                {
                    grid.GridQueue.AddFirst(shipment);
                    //LogGridQueueUpdate(grid, shipment, "addedFirst");
                }
                else
                {
                    grid.GridQueue.AddLast(shipment);
                    //LogGridQueueUpdate(grid, shipment, "addedLast");
                }
                return;
            }

            var candidates = grid.Ports
                .Where(p => p.Status != PortStatus.Closed &&
                            p.CanAccept(shipment) &&
                            p.ShipmentQueue.Count == minQueueSize)
                .OrderByDescending(p => p.HandlingFlags.Count) // prefer specialized ports
                .ToList();

            // among equally specialized ports at same queue size, pick randomly
            int maxFlags = candidates.First().HandlingFlags.Count;
            var topCandidates = candidates.Where(p => p.HandlingFlags.Count == maxFlags).ToList();
            var port = topCandidates[_rng.Next(topCandidates.Count)];

            // insert priority shipments at front, others at back
            if (shipment.IsPriority)
            {
                port.ShipmentQueue.AddFirst(shipment);
            }
            else
            {
                port.ShipmentQueue.AddLast(shipment);
            }

            shipment.Status = ShipmentStatus.Picking;
            /*LogEvent("ShipmentAssignedToPort", _now, new
            {
                shipment_id = shipment.Id,
                port_id = port.Id,
                grid_id = grid.Id,
                queue_size = port.ShipmentQueue.Count
            });*/

            if (port.Status == PortStatus.Idle)
                StartNextPick(port);
        }

        // bins
        private void ScheduleBinTransfers(Shipment shipment/*, ref double binStatusChange, ref double binTransfer*/)
        {
            var destinationGridId = shipment.PackingGridId;
            var transferredFromGrids = new HashSet<string>(); // track which grids we've balanced

            foreach (var pick in shipment.Picks)
            {
                _binById.TryGetValue(pick.BinId, out var bin);
                if (bin == null || bin.GridId == destinationGridId) continue;

                double transferTime = GetTransferTime(bin.GridId, destinationGridId);
                string sourceGridId = bin.GridId;
                var t1 = Stopwatch.GetTimestamp();
                SetBinStatus(bin, BinStatus.Outside);
                //binStatusChange += Stopwatch.GetElapsedTime(t1).TotalMilliseconds;

                _events.Schedule(SimEventType.BinTransferCompleted, _now + transferTime,
                    new BinTransferData(bin, sourceGridId, destinationGridId, transferTime));

                LogEvent("BinTransferStarted", _now, new
                {
                    bin_id = bin.Id,
                    source_grid = sourceGridId,
                    dest_grid = destinationGridId,
                    transfer_duration = transferTime
                });

                // Level 9 - only one return transfer per source grid
                var t2 = Stopwatch.GetTimestamp();
                if (transferredFromGrids.Add(sourceGridId))
                    ScheduleReturnTransfer(destinationGridId, sourceGridId);
                //binTransfer += Stopwatch.GetElapsedTime(t2).TotalMilliseconds;
            }
        }

        private HashSet<string> _reservedBinIds = new();
        private void ScheduleReturnTransfer(string fromGridId, string toGridId)
        {
            var returnBin = _binsByGrid.TryGetValue(fromGridId, out var gridBins)
                ? gridBins.FirstOrDefault(b => b.Status == BinStatus.Available &&
                                               !_reservedBinIds.Contains(b.Id))
                : null;

            if (returnBin == null)
            {
                Console.Error.WriteLine($"No available bin in {fromGridId} to return to {toGridId}");
                return;
            }

            double transferTime = GetTransferTime(fromGridId, toGridId);
            SetBinStatus(returnBin, BinStatus.Outside);
            _reservedBinIds.Add(returnBin.Id);

            _events.Schedule(SimEventType.BinTransferCompleted, _now + transferTime,
                new BinTransferData(returnBin, fromGridId, toGridId, transferTime));

            LogEvent("BinTransferStarted", _now, new
            {
                bin_id = returnBin.Id,
                source_grid = fromGridId,
                dest_grid = toGridId,
                transfer_duration = transferTime
            });
        }

        private void StartNextPick(Port port)
        {
            //Console.Error.WriteLine($"StartNextPick: {port.Id}, queue={port.ShipmentQueue.Count}, status={port.Status}");
            if (port.ShipmentQueue.Count == 0)
            {
                SetPortStatus(port, PortStatus.Idle);
                return;
            }

            var shipment = port.ShipmentQueue.First!.Value;
            port.CurrentShipment = shipment;
            SetPortStatus(port, PortStatus.Busy);

            // find next bin to pick from
            var nextPick = shipment.Picks.FirstOrDefault(p => !p.Completed);
            if (nextPick == null) return;

            _binById.TryGetValue(nextPick.BinId, out var bin);
            if (bin == null) return;

            if (bin.Status == BinStatus.Available)
            {
                //bin.Status = BinStatus.Reserved;
                SetBinStatus(bin, BinStatus.Reserved);
                bin.ReservedForPortId = port.Id;
                LogEvent("BinReserved", _now, new
                {
                    bin_id = bin.Id,
                    port_id = port.Id,
                    shipment_id = shipment.Id
                });

                double deliveryTime = BinDeliveryTime(
                    _grids.First(g => g.Id == port.GridId));

                _events.Schedule(SimEventType.BinArrivedAtPort, _now + deliveryTime,
                    new BinArrivedData(bin, port, shipment));

                LogEvent("BinRequestedAtPort", _now, new
                {
                    bin_id = bin.Id,
                    port_id = port.Id,
                    shipment_id = shipment.Id,
                    estimated_delivery_time = deliveryTime
                });
            }
            else
            {
                // bin is busy, join waiting list
                bin.WaitingPorts.Enqueue(port.Id);
                LogEvent("BinRequestedAtPort", _now, new
                {
                    bin_id = bin.Id,
                    port_id = port.Id,
                    shipment_id = shipment.Id,
                    estimated_delivery_time = -1 // unknown, waiting
                });
            }
        }
        
        private double GetTransferTime(string sourceGridId, string destGridId)
        {
            var transfer = _parameters.TransfersConveyors.Durations
                .FirstOrDefault(t => (t.From == sourceGridId && t.To == destGridId) ||
                                     (t.From == destGridId && t.To == sourceGridId));

            double baseTime = transfer?.Duration ?? 120;
            return Randomize(baseTime,
                _parameters.TransfersConveyors.DurationRandomness.Min,
                _parameters.TransfersConveyors.DurationRandomness.Max);
        }
        #endregion

        #region helpers
        private static string NormalizeSortingDirection(string direction) =>
            direction.ToLowerInvariant().Replace("-", "").Replace(" ", "").Replace("_", "");

        private double Randomize(double base_, double min, double max) =>
            base_ * (_rng.NextDouble() * (max - min) + min);

        private double BinDeliveryTime(Grid grid)
        {
            double baseTime = _parameters.GridBinDelivery.DeliveryTimes.TryGetValue(grid.Id, out double t) ? t : 5;
            return Randomize(baseTime, _parameters.GridBinDelivery.Randomness.Min, _parameters.GridBinDelivery.Randomness.Max);
        }

        private double PickDuration(Shipment shipment, int quantity) =>
            Randomize(quantity / (shipment.IsFragile ? _parameters.FragileThroughput : _parameters.StandardThroughput) * 3600, 0.8, 1.2);

        private double ToSimSeconds(DateTime dt) =>
            (dt - _parameters.SimulationStartTime).TotalSeconds;

        #endregion

        #region logging
        public void InitLog(string path)
        {
            _logWriter = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8)
            {
                AutoFlush = false // don't flush every write
            };
        }

        private void LogEvent(string eventType, double simTime, object data)
        {
            DateTime timestamp;
            try
            {
                timestamp = _parameters.SimulationStartTime.AddSeconds(simTime);
            }
            catch
            {
                timestamp = _parameters.SimulationStartTime;
            }

            var entry = new
            {
                simTime = (int)simTime,
                timestamp = timestamp.ToString("o"),
                @event = eventType,
                data
            };
            _logWriter?.WriteLine(JsonSerializer.Serialize(entry));
            //_logWriter?.Flush();
        }

        private void LogGridQueueUpdate(Grid grid, Shipment shipment, string action)
        {
            LogEvent("GridQueueUpdated", _now, new
            {
                grid_id = grid.Id,
                shipment_id = shipment.Id,
                action = action, // "added" or "removed"
                queue_size = grid.GridQueue.Count
            });
        }

        public void LogFinalMetrics()
        {
            var shipped = _shipments.Where(s => s.Status == ShipmentStatus.Shipped).ToList();
            var packed = _shipments.Where(s => s.Status == ShipmentStatus.Packed).ToList();
            var notPacked = _shipments.Where(s => s.Status != ShipmentStatus.Packed &&
                                                  s.Status != ShipmentStatus.Shipped).ToList();
            var failed = _shipments.Where(s => s.Status == ShipmentStatus.Failed).ToList();

            LogEvent("SimulationComplete", _now, new
            {
                total_shipments = _shipments.Count,
                shipped = shipped.Count,
                packed_not_shipped = packed.Count,
                not_packed = notPacked.Count,
                failed = failed.Count,
                packed_on_time = shipped.Count(s =>
                    (s.CompletionTime!.Value - s.CreatedAt).TotalHours <= 24),
                avg_lead_time_hours = shipped.Any()
                    ? Math.Round(shipped.Average(s =>
                        (s.ShippedTime!.Value - s.CreatedAt).TotalHours), 2)
                    : 0,
                avg_dwell_time_hours = shipped.Any()
                    ? Math.Round(shipped.Average(s =>
                        (s.ShippedTime!.Value - s.CompletionTime!.Value).TotalHours), 2)
                    : 0
            });
        }
        #endregion
    }
}