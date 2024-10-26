using System.Net.Http.Json;

namespace WebUI.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "api/documents";

        public DocumentService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GetHelloWorldAsync()
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<string>($"{BaseUrl}/hello");
                return response;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to get Hello World: {ex.Message}");
            }
        }
    }
}