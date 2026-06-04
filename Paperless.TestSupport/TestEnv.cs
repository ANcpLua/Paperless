namespace Paperless.TestSupport;

/// <summary>
///     One-time <c>.env.test</c> loading and environment-variable image resolution
///     shared by every integration fixture. Replaces the three duplicated static
///     constructors (REST shared fixture, REST <c>DatabaseFixture</c>, Services fixture).
/// </summary>
public static class TestEnv
{
	private static readonly Lock Gate = new();
	private static bool _loaded;

	/// <summary>
	///     Loads <c>.env.test</c> exactly once per process via
	///     <see cref="Env.TraversePath" />. Idempotent and thread-safe so it can be
	///     called from every fixture's static constructor without re-loading.
	/// </summary>
	public static void Load()
	{
		if (_loaded) return;
		lock (Gate)
		{
			if (_loaded) return;
			Env.TraversePath().Load(".env.test");
			_loaded = true;
		}
	}

	/// <summary>
	///     Returns the container image for <paramref name="envVar" />, falling back to
	///     <paramref name="defaultImage" /> when the variable is unset. Mirrors the
	///     <c>Environment.GetEnvironmentVariable(...) ?? "image:tag"</c> pattern the
	///     fixtures duplicated per container.
	/// </summary>
	public static string Image(string envVar, string defaultImage) =>
		Environment.GetEnvironmentVariable(envVar) ?? defaultImage;
}
