# Decision — first day of week, clock and date order are settings, each with its own System variant

**Date:** 2026-10-09
**Scope:** `ArctisAurora.Core.UI` — `DateTimeSettings`, `DateFormats`, `CalendarControl`, `GanttChartControl`, `PlannerBoardControl`, `NotePropertiesControl`; tests in `PlannerTests` (Thorium)

## What changed
- NEW `DateTimeSettings` (`[A_XSDType("DateTime","Settings")]`, `SettingCategory`): `weekStart` (`WeekStartSetting`, `<WeekStart Day>`), `timeFormat` (`TimeFormatSetting`, `<TimeFormat Clock>`), `dateFormat` (`DateFormatSetting`, `<DateFormat Order>`); all `onChanged = "Planner.Reformat"`, all default `System`.
- Enums: `WeekDay` {System, Monday..Sunday}, `ClockFormat` {System, H24, H12}, `DateOrder` {System, DayMonthYear, MonthDayYear, YearMonthDay}.
- No settings XML file added — defaults resolve without one. The category shows in the Settings window automatically (enums become dropdowns).
- NEW static `DateFormats`: `FirstDay`, `WeekStart(DateOnly)`, `WeekStart(DateTime)`, `Time`, `Hour`, `ShortHour`, `Date`, `DayMonth(DateTime, bool year)`.
- `DateFormats.Reformat` = `[A_XSDActionDependency("Planner.Reformat","Settings")]`: walks every window's UI tree; `PlannerEditorControl` -> `chart.RowsChanged` / `board.RebuildSoon` / `calendar.Changed`; `NotePropertiesControl` -> `Refresh`.
- Consumers: `CalendarControl.first` (was hard-coded Monday), hour gutter, month chips, expanded card title; `GanttChartControl.Floor` Week unit and Hour labels; `PlannerBoardControl.Dates`; `NotePropertiesControl.Date` (Created/Modified display).
- Output samples: `Time` "14:30" / "2:30 PM"; `Hour` "14:00" / "2 PM"; `ShortHour` "14" / "2PM"; `DayMonth` "7 Oct 2026" / "Oct 7, 2026" / "2026 Oct 7"; `Date` digits (System = culture ShortDatePattern, else dd/MM/yyyy, MM/dd/yyyy, yyyy-MM-dd).
- Calendar: week-start tint — see the planner note, section "Calendar snap and week start"; the snap now starts on scroll gesture end, see [[scroll-gesture-end]].

## Why these choices

**Each setting has its own `System` variant, not one global "use system" checkbox plus three custom fields.**
A user can follow the OS week start and still pick their own clock. The Settings window's enum-to-dropdown editor covers it with no new editor code.

**System values come from `CultureInfo.CurrentCulture.DateTimeFormat`.**
FirstDayOfWeek; ShortTimePattern containing 'h' means 12-hour; the order of y/M/d in ShortDatePattern. On Windows CurrentCulture carries the user's Region overrides (checked on this machine: en-US with Monday and HH:mm). Weekday and month names stay invariant English; only order and clock follow the setting.

**The category lives in the engine, not Thorium.**
`CalendarControl` is an engine control, so the category also appears in Carbon/Editor settings.

**Scope is the planner surfaces plus the note-properties Created/Modified display.**
The ticket popup's editable yyyy-MM-dd fields are left alone because they are parsed back. File formats (planner XML, note frontmatter, ISO) are unchanged.

## Known gaps
- Weekday and month names are not localised.
- Gantt Hour labels in H12 ("12AM") not checked for fit at Hour zoom.
- Not checked: the Settings window rows by GUI, live `Planner.Reformat` on a running app, the System branch on a non-en-US culture.
- Tests pin Monday / H24 / DayMonthYear via `PinDates(WeekDay, ClockFormat)` in `Show`, so results do not follow machine culture.

Related: [[planner]], [[settings-categories]], [[settings-registry]]
