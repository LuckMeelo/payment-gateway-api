using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests;

public class PaymentLoggingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    // Must match the API's naming policy so model binding succeeds
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public PaymentLoggingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Security: card data must never appear in log output

    [Fact]
    public async Task ProcessPayment_DoesNotLogFullCardNumberOrCvv()
    {
        // Arrange — use distinctive values that are easy to detect if they leak
        const string fullCardNumber = "2222405343248877"; // last four: 8877
        const string cvv = "9876";

        var logCapture = new LogCapture();

        var bankStub = Substitute.For<IAcquiringBankClient>();
        bankStub
            .AuthorizeAsync(Arg.Any<BankAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BankAuthorizationResult(BankResponseStatus.Authorized, "code")));

        var client = _factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAcquiringBankClient>();
                services.AddSingleton(bankStub);
                // Registering ILoggerProvider in DI is equivalent to AddProvider:
                // LoggerFactory discovers all ILoggerProvider instances from the container.
                // This runs after Program.cs's ClearProviders, so it is not removed.
                services.AddSingleton<ILoggerProvider>(logCapture);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = fullCardNumber,
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 100,
            Cvv = cvv
        };

        // Act
        await client.PostAsJsonAsync("/api/payments", request, SnakeCaseOptions);

        // Assert - neither the full PAN nor the CVV must appear anywhere in log output
        // (last-four "8877" is allowed — it is safe to store and log)
        var captured = logCapture.AllValues;
        Assert.DoesNotContain(captured, entry => entry.Contains(fullCardNumber));
        Assert.DoesNotContain(captured, entry => entry.Contains(cvv));
    }

    // Log-capture infrastructure

    /// <summary>
    /// ILoggerProvider that collects every formatted message and every structured
    /// state value written by any logger category.
    /// </summary>
    private sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentBag<string> _values = new();

        public IReadOnlyCollection<string> AllValues => _values;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_values);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<string> values) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Capture the final formatted string
                var message = formatter(state, exception);
                if (!string.IsNullOrEmpty(message))
                    values.Add(message);

                // Capture every individual structured argument so we catch
                // values that might not appear in the format string verbatim
                // (e.g. a future {CardNumber} template argument would be caught here)
                if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
                {
                    foreach (var kv in structuredState)
                    {
                        var val = kv.Value?.ToString();
                        if (!string.IsNullOrEmpty(val))
                            values.Add(val);
                    }
                }
            }
        }
    }
}