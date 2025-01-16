using Contract;
using EasyNetQ;

namespace Tests.EasyNetQTest;

[TestFixture]
public class EasyNetQTest
{
    private const string TestPdfName = "HelloWorld.pdf";

    [Test]
    public Task PublishDocument_UsingEasyNetQ_ShouldSucceed()
    {
        // Arrange
        using var bus = RabbitHutch.CreateBus("host=localhost");
        var documentUploadedEvent = new DocumentUploadedEvent
        {
            DocumentId = 1,
            FileName = TestPdfName
        };

        // Act & Assert
        Assert.That(
            async () => await bus.PubSub.PublishAsync(documentUploadedEvent),
            Throws.Nothing,
            "Publishing message to RabbitMQ should not throw any exceptions"
        );
        return Task.CompletedTask;
    }
}
