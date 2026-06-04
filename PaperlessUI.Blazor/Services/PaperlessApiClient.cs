using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using PaperlessUI.Blazor.Models;

namespace PaperlessUI.Blazor.Services;

/// <summary>REST calls to PaperlessREST's document API. One instance per request (transient typed client).</summary>
public sealed class PaperlessApiClient(HttpClient http)
{
    public const long MaxUploadBytes = 50L * 1024 * 1024; // matches nginx client_max_body_size 50m

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DocumentDto>> GetDocumentsAsync(CancellationToken ct = default)
    {
        var page = await http.GetFromJsonAsync<PaginatedDocumentsResponse>("/api/v1/documents", s_json, ct);
        return page?.Items ?? [];
    }

    public async Task<IReadOnlyList<DocumentDto>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        var url = $"/api/v1/documents/search?query={Uri.EscapeDataString(query)}&limit={limit}";
        var hits = await http.GetFromJsonAsync<List<DocumentSearchResultDto>>(url, s_json, ct);
        return hits?.ConvertAll(static h => new DocumentDto
        {
            Id = h.Id,
            FileName = h.FileName,
            Status = h.Status,
            CreatedAt = h.CreatedAt,
            Content = h.Content,
            Summary = h.Summary
        }) ?? [];
    }

    public async Task<(CreateDocumentResponse? Doc, string? Error)> UploadAsync(IBrowserFile file, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream(MaxUploadBytes, ct);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", file.Name);

        using var resp = await http.PostAsync("/api/v1/documents", content, ct);
        if (resp.IsSuccessStatusCode)
        {
            var created = await resp.Content.ReadFromJsonAsync<CreateDocumentResponse>(s_json, ct);
            return (created, null);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        return (null, $"{(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var resp = await http.DeleteAsync($"/api/v1/documents/{id}", ct);
        return resp.IsSuccessStatusCode;
    }
}
