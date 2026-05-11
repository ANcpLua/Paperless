using Nuke.Common.Tooling;
using System.ComponentModel;

namespace Build.Components;

/// <summary>
///     Build configuration enumeration (Debug/Release).
/// </summary>
[TypeConverter(typeof(TypeConverter<Configuration>))]
internal sealed class Configuration : Enumeration
{
	public static readonly Configuration Debug = new() { Value = nameof(Debug) };
	public static readonly Configuration Release = new() { Value = nameof(Release) };

	public static implicit operator string(Configuration c) => c.Value;
}
