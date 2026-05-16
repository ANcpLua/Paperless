using Nuke.Common.IO;
using System;
using System.Collections.Generic;
using System.Text;

namespace Build;

/// <summary>
///     Microsoft Testing Platform extensions for NUKE (.NET 10+ / xUnit v3).
/// </summary>
/// <remarks>
///     xUnit v3 MTP filter options (passed after --):
///     --filter-namespace, --filter-class, --filter-method, --filter-trait, --filter-query
///     See: https://xunit.net/docs/query-filter-language
/// </remarks>
public static class MtpExtensions
{
	public static MtpArgumentsBuilder Mtp() => new();
}

/// <summary>
///     Fluent builder for xUnit v3 MTP command-line arguments.
/// </summary>
public sealed class MtpArgumentsBuilder
{
	private readonly List<string> _args = [];

	// ── Private Helpers ──────────────────────────────────────────────────────

	private MtpArgumentsBuilder AddFilter(string option, params ReadOnlySpan<string> patterns)
	{
		foreach (string p in patterns)
		{
			if (string.IsNullOrEmpty(p))
			{
				continue;
			}

			_args.Add(option);
			_args.Add(p);
		}

		return this;
	}

	private MtpArgumentsBuilder AddOption(string option, string value)
	{
		_args.Add(option);
		_args.Add(value);
		return this;
	}

	private MtpArgumentsBuilder AddFlag(string option)
	{
		_args.Add(option);
		_args.Add("on");
		return this;
	}

	// ── Namespace Filters ────────────────────────────────────────────────────

	public MtpArgumentsBuilder FilterNamespace(params ReadOnlySpan<string> patterns)
		=> AddFilter("--filter-namespace", patterns);

	public MtpArgumentsBuilder FilterNotNamespace(params ReadOnlySpan<string> patterns)
		=> AddFilter("--filter-not-namespace", patterns);

	// ── Class/Method Filters ─────────────────────────────────────────────────

	public MtpArgumentsBuilder FilterClass(params ReadOnlySpan<string> patterns)
		=> AddFilter("--filter-class", patterns);

	public MtpArgumentsBuilder FilterNotClass(params ReadOnlySpan<string> patterns)
		=> AddFilter("--filter-not-class", patterns);

	public MtpArgumentsBuilder FilterMethod(params ReadOnlySpan<string> patterns)
		=> AddFilter("--filter-method", patterns);

	public MtpArgumentsBuilder FilterTrait(string name, string value)
		=> AddOption("--filter-trait", $"{name}={value}");

	public MtpArgumentsBuilder FilterNotTrait(string name, string value)
		=> AddOption("--filter-not-trait", $"{name}={value}");

	// ── Query Filter ─────────────────────────────────────────────────────────

	public MtpArgumentsBuilder FilterQuery(string? expression)
		=> string.IsNullOrEmpty(expression) ? this : AddOption("--filter-query", expression);

	// ── Reports ──────────────────────────────────────────────────────────────

	public MtpArgumentsBuilder ReportTrx(string filename)
	{
		_args.Add("--report-trx");
		return AddOption("--report-trx-filename", filename);
	}

	public MtpArgumentsBuilder ReportXunit(string filename)
	{
		_args.Add("--report-xunit");
		return AddOption("--report-xunit-filename", filename);
	}

	public MtpArgumentsBuilder ReportJunit(string filename)
	{
		_args.Add("--report-junit");
		return AddOption("--report-junit-filename", filename);
	}

	// ── Execution Control ────────────────────────────────────────────────────

	public MtpArgumentsBuilder StopOnFail() => AddFlag("--stop-on-fail");

	public MtpArgumentsBuilder MaxThreads(int count)
		=> AddOption("--max-threads", count.ToString());

	public MtpArgumentsBuilder Timeout(TimeSpan duration)
		=> AddOption("--timeout", $"{(int)duration.TotalSeconds}s");

	public MtpArgumentsBuilder IgnoreExitCode(int code)
		=> AddOption("--ignore-exit-code", code.ToString());

	public MtpArgumentsBuilder MinimumExpectedTests(int count)
		=> AddOption("--minimum-expected-tests", count.ToString());

	public MtpArgumentsBuilder Seed(int seed)
		=> AddOption("--seed", seed.ToString());

	// ── Diagnostics ──────────────────────────────────────────────────────────

	public MtpArgumentsBuilder ShowLiveOutput() => AddFlag("--show-live-output");

	public MtpArgumentsBuilder Diagnostics() => AddFlag("--xunit-diagnostics");

	// ── Coverage ─────────────────────────────────────────────────────────────

	public MtpArgumentsBuilder CoverageCobertura(AbsolutePath outputPath)
	{
		_args.Add("--coverage");
		_args.Add("--coverage-output-format");
		_args.Add("cobertura");
		return AddOption("--coverage-output", outputPath.ToString());
	}

	// ── Build ────────────────────────────────────────────────────────────────

	public string Build()
	{
		if (_args is [])
		{
			return string.Empty;
		}

		StringBuilder sb = new();
		foreach (string arg in _args)
		{
			if (sb.Length > 0)
			{
				sb.Append(' ');
			}

			sb.Append(arg.Contains(' ') ? $"\"{arg}\"" : arg);
		}

		return sb.ToString();
	}

	public IReadOnlyList<string> BuildArgs() => _args;
}
