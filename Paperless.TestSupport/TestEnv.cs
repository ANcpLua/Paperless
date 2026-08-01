namespace Paperless.TestSupport;

/// <summary>Loads <c>.env.test</c> once and resolves container images from environment variables.</summary>
public static class TestEnv
{
	private static readonly Lock s_gate = new();
	private static bool s_loaded;

	/// <summary>Loads <c>.env.test</c> exactly once per process.</summary>
	public static void Load()
	{
		lock (s_gate)
		{
			if (s_loaded) return;
			Env.TraversePath().Load(".env.test");
			s_loaded = true;
		}
	}

	/// <summary>Returns an environment-selected container image or its default.</summary>
	public static string Image(string envVar, string defaultImage) =>
		Environment.GetEnvironmentVariable(envVar) ?? defaultImage;
}
