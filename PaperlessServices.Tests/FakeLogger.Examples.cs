// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Logging.Testing;
// using Xunit;
//
// namespace Paperless.UnitTests.Helpers.Examples;
//
// /// <summary>
// /// Examples demonstrating the extensible FakeLogger architecture
// /// </summary>
// public class FakeLoggerExamples
// {
//     [Fact]
//     public void BasicUsage_SimpleVerification()
//     {
//         // Arrange
//         var logger = new FakeLogger<MyService>();
//         var service = new MyService(logger);
//
//         // Act
//         service.ProcessOrder(123);
//
//         // Assert - Simple API
//         logger.Verify(
//             LogVerifiers.HasLog(LogLevel.Information, "Processing order {OrderId}"),
//             LogVerifiers.HasLog(LogLevel.Information, "Order processed successfully")
//         );
//     }
//
//     [Fact]
//     public void FluentApi_ChainedAssertions()
//     {
//         // Arrange
//         var logger = FakeLogger.CreateStrict<MyService>();
//         var service = new MyService(logger);
//
//         // Act
//         service.ProcessOrderWithValidation(123, 99.99m);
//
//         // Assert - Fluent API
//         logger.Should()
//             .HaveLoggedInformation("Starting order validation")
//             .HaveLoggedDebug("Order amount: {Amount}")
//             .HaveLoggedInformation("Order {OrderId} processed")
//             .And();
//     }
//
//     [Fact]
//     public void StructuredLogging_VerifyProperties()
//     {
//         // Arrange
//         var logger = new FakeLogger<MyService>();
//         var service = new MyService(logger);
//
//         // Act
//         service.ProcessOrderWithDetails(123, "CUST-456", 149.99m);
//
//         // Assert - Verify structured properties
//         logger.Verify()
//             .HasLogWithProperties(
//                 LogLevel.Information,
//                 "Order processed",
//                 ("OrderId", 123),
//                 ("CustomerId", "CUST-456"),
//                 ("Amount", 149.99m))
//             .Execute();
//     }
//
//     [Fact]
//     public void ExceptionLogging_VerifyExceptionType()
//     {
//         // Arrange
//         var logger = new FakeLogger<MyService>();
//         var service = new MyService(logger);
//
//         // Act & Assert
//         Assert.Throws<InvalidOperationException>(() => service.ProcessInvalidOrder());
//
//         logger.Verify()
//             .HasLog<InvalidOperationException>(
//                 LogLevel.Error,
//                 "Failed to process order")
//             .Execute();
//     }
//
//     [Fact]
//     public void SequenceVerification_EnsureCorrectOrder()
//     {
//         // Arrange
//         var logger = new FakeLogger<MyService>();
//         var service = new MyService(logger);
//
//         // Act
//         service.ProcessOrderWithSteps(123);
//
//         // Assert - Verify sequence
//         logger.Verify()
//             .HasSequence(
//                 (LogLevel.Information, "Step 1: Validating order"),
//                 (LogLevel.Information, "Step 2: Checking inventory"),
//                 (LogLevel.Information, "Step 3: Processing payment"),
//                 (LogLevel.Information, "Step 4: Order complete"))
//             .Execute();
//     }
// }

