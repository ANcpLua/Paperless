namespace Tests.IntegrationTests;

public class TestBase
{
    protected HttpClient Client;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8081")
        };
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Client.Dispose();
    }
}