using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.UI
{
    [A_XSDType("WeekDay", "Settings")]
    public enum WeekDay
    {
        System, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday
    }

    [A_XSDType("ClockFormat", "Settings")]
    public enum ClockFormat
    {
        System, H24, H12
    }

    [A_XSDType("DateOrder", "Settings")]
    public enum DateOrder
    {
        System, DayMonthYear, MonthDayYear, YearMonthDay
    }

    [A_XSDType("WeekStart", "Settings")]
    public class WeekStartSetting : Setting
    {
        [A_XSDElementProperty("Day", "Settings", "First day of a calendar week, or the system's.")]
        public WeekDay day { get; set; } = WeekDay.System;
    }

    [A_XSDType("TimeFormat", "Settings")]
    public class TimeFormatSetting : Setting
    {
        [A_XSDElementProperty("Clock", "Settings", "24-hour or 12-hour times, or the system's.")]
        public ClockFormat clock { get; set; } = ClockFormat.System;
    }

    [A_XSDType("DateFormat", "Settings")]
    public class DateFormatSetting : Setting
    {
        [A_XSDElementProperty("Order", "Settings", "Order of day, month and year in shown dates, or the system's.")]
        public DateOrder order { get; set; } = DateOrder.System;
    }

    [A_XSDType("DateTime", "Settings", AllowedChildren = typeof(Setting))]
    public class DateTimeSettings : SettingCategory
    {
        public readonly WeekStartSetting weekStart = new WeekStartSetting { onChanged = "Planner.Reformat" };
        public readonly TimeFormatSetting timeFormat = new TimeFormatSetting { onChanged = "Planner.Reformat" };
        public readonly DateFormatSetting dateFormat = new DateFormatSetting { onChanged = "Planner.Reformat" };
    }
}
