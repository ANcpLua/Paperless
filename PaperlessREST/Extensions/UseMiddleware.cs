namespace PaperlessREST.Extensions;

public static class UseMiddleware
{
    public static void ConfigureMiddleware(this WebApplication app)
    {
        app.UseStaticFiles();  // must precede Scalar UI        
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseHttpLogging();
    }
}