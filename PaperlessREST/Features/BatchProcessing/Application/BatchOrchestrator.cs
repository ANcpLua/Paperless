namespace PaperlessREST.Features.BatchProcessing.Application;

public interface IReportProcessor
{
	Task<ErrorOr<ProcessingResult>> ProcessAsync(string path, CancellationToken ct);
}

public sealed record ProcessingResult(
	int ProcessedCount,
	int SkippedCount);

public sealed class BatchOrchestrator(
	IOptions<BatchOptions> options,
	IFileSystem fs,
	TimeProvider time,
	IReportProcessor processor,
	ILogger<BatchOrchestrator> logger)
{
	private const string ProcessingExt = ".processing";
	private const string FailedExt = ".failed";
	private readonly BatchOptions _opts = options.Value;

	[DisableConcurrentExecution(3600)]
	[AutomaticRetry(Attempts = 3, DelaysInSeconds = [20, 60, 300])]
	public async Task ProcessAsync(IJobCancellationToken token)
	{
		logger.LogInformation(
			"Batch job '{JobId}' started. Pattern: '{Pattern}', Input: '{InputPath}'",
			BatchOptions.JobId, _opts.FilePattern, _opts.InputPath);

		List<string> paths = ClaimFiles();
		int processed = 0, quarantined = 0;

		switch (paths.Count)
		{
			case 0:
				logger.LogDebug("Batch job '{JobId}' found no files", BatchOptions.JobId);
				break;
			case > 0:
				logger.LogDebug("Batch job '{JobId}' processing {Count} file(s)", BatchOptions.JobId, paths.Count);
				break;
		}

		foreach (string path in paths)
		{
			token.ThrowIfCancellationRequested();

			if (await ProcessFileAsync(path, token.ShutdownToken))
			{
				processed++;
				continue;
			}

			quarantined++;
		}

		logger.LogInformation(
			"Batch job '{JobId}' completed: {Processed} processed, {Quarantined} quarantined",
			BatchOptions.JobId, processed, quarantined);
	}

	private List<string> ClaimFiles()
	{
		IDirectoryInfo inputDir = fs.DirectoryInfo.New(_opts.InputPath);
		inputDir.Create();

		List<string> claimed = [];

		ReclaimOrphanedFiles(inputDir, claimed);
		ClaimNewFiles(inputDir, claimed);

		return claimed;
	}

	private void ReclaimOrphanedFiles(IDirectoryInfo inputDir, List<string> claimed)
	{
		foreach (IFileInfo orphan in inputDir.EnumerateFiles($"*{ProcessingExt}"))
		{
			logger.LogInformation("Reclaiming orphaned file: {File}", orphan.Name);
			claimed.Add(orphan.FullName);
		}
	}

	private void ClaimNewFiles(IDirectoryInfo inputDir, List<string> claimed)
	{
		foreach (IFileInfo file in inputDir.EnumerateFiles(_opts.FilePattern))
		{
			string claimedPath = $"{file.FullName}{ProcessingExt}";

			try
			{
				fs.File.Move(file.FullName, claimedPath, false);
				logger.LogInformation("Claimed file: {File}", file.Name);
				claimed.Add(claimedPath);
			}
			catch (IOException ex)
			{
				logger.LogWarning(ex, "Could not claim '{File}'", file.Name);
			}
		}
	}

	internal async Task<bool> ProcessFileAsync(string path, CancellationToken ct)
	{
		string originalName = Path.GetFileName(path).Replace(ProcessingExt, "");

		logger.LogInformation("Processing file: {File}", originalName);

		ErrorOr<ProcessingResult> result = await processor.ProcessAsync(path, ct);

		if (result.IsError)
		{
			logger.LogError(
				"File {File} quarantined: [{Code}] {Description}",
				originalName, result.FirstError.Code, result.FirstError.Description);

			MoveFileOrThrow(path, _opts.ErrorPath, originalName, FailedExt);
			return false;
		}

		MoveFileOrThrow(path, _opts.ArchivePath, originalName);

		logger.LogInformation(
			"Successfully processed {File}: {Processed} processed, {Skipped} skipped",
			originalName, result.Value.ProcessedCount, result.Value.SkippedCount);

		return true;
	}

	private void MoveFileOrThrow(string sourcePath, string destinationDir, string originalName,
		string statusSuffix = "")
	{
		fs.DirectoryInfo.New(destinationDir).Create();

		DateTime timestamp = time.GetUtcNow().UtcDateTime;
		string destFileName = $"{originalName}.{timestamp:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}{statusSuffix}";
		string destPath = fs.Path.Combine(destinationDir, destFileName);

		if (!fs.File.Exists(sourcePath))
		{
			logger.LogWarning("Source file no longer exists: {File}", sourcePath);
			return;
		}

		try
		{
			fs.File.Move(sourcePath, destPath);
			logger.LogInformation("Moved file to: {Destination}", destPath);
		}

		catch (Exception ex)
		{
			string message = $"Infrastructure error moving file '{sourcePath}' to '{destPath}'";
			logger.LogError(ex, "{Message} - Hangfire will retry", message);
			throw new IOException(message, ex);
		}
	}
}
