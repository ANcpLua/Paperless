// namespace PaperlessREST;
//
// public class keyedservice
// {
//     // Program.cs
//     var builder = WebApplication.CreateBuilder(args);
//     builder.Services.AddHttpClient("github", client =>
//     {
//         client.BaseAddress = new Uri("https://api.github.com/");
//         client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
//         client.DefaultRequestHeaders.Add("User-Agent", "MyApp");
//     }).AddAsKeyed(); // Enables [FromKeyedServices] injection
//
//     var app = builder.Build();
//     app.MapGet("/repos/{owner}/{repo}", 
//     ([FromKeyedServices("github")] HttpClient github, string owner, string repo) =>
//     github.GetFromJsonAsync<object>($"repos/{owner}/{repo}")
//     );
//     app.Run();
//
// }