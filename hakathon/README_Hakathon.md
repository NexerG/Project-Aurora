# Warehouse Simulator

## Running

```bash
hakathon.exe [options]
```

## Rundown
The Warehouse Simulator is a discrete-event simulation engine made in C# that models the operations of an automated warehouse from order receipt to shipment dispatch. It processes shipments through a defined lifecycle — Received, Routed, Consolidation, Ready, Picking, Packed, Shipped — driven by an event queue ordered by simulation time. Shipments are assigned to grids and packing ports by an external or built-in router, which optimizes bin selection and minimizes inter-grid transfers. Bins hold physical inventory and can be transferred between grids via conveyors when items needed for a shipment are spread across multiple storage areas. Ports operate within defined shifts and break schedules, with busy ports entering a PendingClose state when a shift ends rather than abandoning work mid-shipment. Trucks arrive on fixed schedules per sorting direction and dispatch all packed shipments assigned to their route. The simulator produces a JSON-lines event log capturing the full timeline of warehouse activity along with final metrics including packed-on-time rate, average lead time, dwell time, and port utilization.

## Main Focus
The main focus was spent on optimising the simulation instead of building a UI system.
A custom router was built and is faster than the default one.
simulation was tried to optimise like "hell" but little time lead to only such optimisations.
	for example: if there are shipments that are impossible to fulfill (no compatible ports or items present in the bins) then the simulation slows down drastically.

## Future possibilities
More optimisation - data types instead of classes
More arguments like simulation step
Visually representing the simulation using either a custom or Unity engine
Visually representing the robots with collisions and pathing

## Options

| Argument | Default |
|---|---|
| `--level=<n>` | `10` by default |
| `--wait` | '0' by default |
| `--dataDir=<path>` | `./data/<level>/` |
| `--eventLogFile=<path>` | `./simulationAdvancedRouter<level>.log` |
| `--router=<path>` | `./build/AdvancedRouter/AdvancedRouter.exe` ONLY USED WHEN VIRTUALIZED IS FALSE |
| `--advanced` | enabled by default |
| `--virtualized` | enabled by default |

## Examples

```bash
# Run level 10 with defaults
hakathon.exe

# Run a specific level
hakathon.exe --level=9

# Run with custom data directory
hakathon.exe --dataDir=C:\data\big

# Run with default router and custom log
hakathon.exe --router=./build/router.exe --eventLogFile=./output.log

# Run with wait (5 seconds)
hakathon.exe --wait=5

```

## Output

Writes a JSON-lines event log. Final line contains simulation metrics:

```json
{"event":"SimulationComplete","data":{"total_shipments":174697,"shipped":34676,"packed_on_time":34672,"avg_lead_time_hours":12.59,...}}
```
