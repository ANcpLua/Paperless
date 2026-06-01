namespace PaperlessREST.Features.BatchProcessing.Application;

public sealed class ReportProcessor(
	IFileSystem fs,
	IDocumentAccessRepository repo,
	ILogger<ReportProcessor> logger) : IReportProcessor
{
	private const string SchemaFileName = "accessReport.xsd";
	private static readonly XmlSerializer s_serializer = new(typeof(AccessReportDto));

	private XmlSchemaSet Schemas => field ??= LoadSchemas();

	public async Task<ErrorOr<ProcessingResult>> ProcessAsync(string path, CancellationToken ct)
	{
		var parseResult = await ParseAndValidateXmlAsync(path);
		if (parseResult.IsError)
		{
			return parseResult.Errors;
		}

		var (dto, date) = parseResult.Value;

		if (dto.Documents.Count is 0)
		{
			logger.LogInformation("Report for {Date} contains no documents", date);
			return new ProcessingResult(0, 0);
		}

		Guid[] docIds = [.. dto.Documents.Select(d => d.Id).Distinct()];
		HashSet<Guid> knownIds = [.. await repo.GetExistingDocumentIdsAsync(docIds, ct)];
		var skippedCount = docIds.Length - knownIds.Count;

		if (skippedCount > 0)
		{
			Guid[] unknown = [.. docIds.Except(knownIds)];
			logger.LogWarning(
				"Report for {Date} references {SkippedCount} unknown documents (will be skipped): {Ids}",
				date, skippedCount, string.Join(", ", unknown.Take(5)));
		}

		(Guid DocumentId, long AccessCount)[] items =
		[
			.. dto.Documents
				.Where(d => knownIds.Contains(d.Id))
				.GroupBy(d => d.Id)
				.Select(g => (DocumentId: g.Key, AccessCount: g.Sum(d => d.Count)))
		];

		await repo.UpsertDailyAccessAsync(date, items, ct);

		return new ProcessingResult(items.Length, skippedCount);
	}

	private XmlSchemaSet LoadSchemas()
	{
		XmlSchemaSet schemas = new();
		var schemaPath = fs.Path.Combine(AppContext.BaseDirectory, "Schemas", SchemaFileName);

		using Stream schemaStream = fs.FileStream.New(schemaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using var schemaReader = XmlReader.Create(schemaStream);
		schemas.Add("", schemaReader);
		schemas.Compile();
		return schemas;
	}

	private async Task<ErrorOr<(AccessReportDto Dto, DateOnly Date)>> ParseAndValidateXmlAsync(string path)
	{
		try
		{
			await using var stream =
				fs.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.Read);

			XmlReaderSettings settings = new()
			{
				Schemas = Schemas,
				ValidationType = ValidationType.Schema,
				DtdProcessing = DtdProcessing.Prohibit,
				IgnoreWhitespace = true,
				Async = true,
				XmlResolver = null
			};

			List<string> validationErrors = [];
			settings.ValidationEventHandler += (_, e) =>
			{
				if (e.Severity == XmlSeverityType.Error)
				{
					validationErrors.Add(e.Message);
				}
			};

			using var reader = XmlReader.Create(stream, settings);
			var dto = (AccessReportDto)s_serializer.Deserialize(reader)!;

			if (validationErrors.Count > 0)
			{
				return ReportErrors.InvalidSchema(string.Join("; ", validationErrors));
			}

			if (!DateOnly.TryParseExact(dto.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
				    DateTimeStyles.None, out var date))
			{
				return ReportErrors.InvalidDate(dto.Date);
			}

			// Empty document list is valid; FindIndex returns -1 and we fall through to success.
			var emptyGuidIndex = dto.Documents.FindIndex(d => d.Id == Guid.Empty);
			if (emptyGuidIndex >= 0)
			{
				return ReportErrors.InvalidGuid(emptyGuidIndex);
			}

			return (dto, date);
		}
		catch (FileNotFoundException)
		{
			return ReportErrors.FileNotFound(path);
		}
		catch (InvalidOperationException ex)
		{
			// Malformed input XML surfaces here: Deserialize wraps the underlying XmlException
			// in an InvalidOperationException (InnerException set), so a dedicated
			// `catch (XmlException)` branch would be unreachable. Schema-validation failures
			// against the input don't throw at all — the ValidationEventHandler above collects
			// them into validationErrors.
			//
			// There is deliberately NO `catch (XmlSchemaException)`: that is thrown raw by
			// XmlSchemaSet.Compile() only when the bundled accessReport.xsd itself is invalid —
			// a deploy bug, not bad input. Letting it propagate fails the Hangfire batch job
			// loudly (and retries) instead of quarantining every file under a bogus
			// "invalid schema" data error.
			return ReportErrors.InvalidSchema(ex.Message);
		}
	}
}
