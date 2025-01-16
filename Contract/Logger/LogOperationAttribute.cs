using Microsoft.Extensions.Logging;

namespace Contract.Logger;

[AttributeUsage(AttributeTargets.Method)]
public class LogOperationAttribute : Attribute
{
    public string Component { get; }
    public string Category { get; }
    public LogLevel Level { get; }
    public bool LogParameters { get; set; } = true;
    public bool LogResponse { get; set; } = true;

    public LogOperationAttribute(string component, string category, LogLevel level = LogLevel.Information)
    {
        Component = component;
        Category = category;
        Level = level;
    }
}
