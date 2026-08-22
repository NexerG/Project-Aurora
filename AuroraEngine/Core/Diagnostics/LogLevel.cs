using ArctisAurora.Core.Registry;

namespace ArctisAurora.Core.Diagnostics
{
    [A_XSDType("LogLevel", "Settings")]
    public enum LogLevel : byte
    {
        Hot,
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        Fatal,
        Off
    }
}
