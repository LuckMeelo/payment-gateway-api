using System.Text.Json;

using Microsoft.Extensions.Logging;

using PaymentGateway.Api.Bank;

// ILogger<T> is available via the project reference; NullLogger<T> requires the Abstractions
// assembly which the SDK exposes at runtime but not compile-time without extra configuration.
// A local no-op satisfies the constructor without any extra package.

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PaymentGateway.Api.Tests;

public class AcquiringBankClientTests
{
    private sealed class NoOpLogger<T> : ILogger<T>
    {
        public static readonly NoOpLogger<T> Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        { }
    }

    private static readonly BankAuthorizationRequest SampleRequest = new()
    {
        CardNumber = "2222405343248871",
        ExpiryDate = "04/2027",
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };

    private static AcquiringBankClient CreateClient(WireMockServer server, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(server.Url!),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
        return new AcquiringBankClient(httpClient, NoOpLogger<AcquiringBankClient>.Instance);
    }

    // ── Happy paths ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_BankAuthorizes_ReturnsAuthorizedWithCode()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"authorized\":true,\"authorization_code\":\"abc-123\"}"));
        var client = CreateClient(server);

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Authorized, result.Status);
        Assert.Equal("abc-123", result.AuthorizationCode);
    }

    [Fact]
    public async Task AuthorizeAsync_BankDeclines_ReturnsDeclined()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"authorized\":false,\"authorization_code\":\"\"}"));
        var client = CreateClient(server);

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Declined, result.Status);
    }

    // ── Bank error responses → Unavailable ───────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_BankReturns503_ReturnsUnavailable()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));
        var client = CreateClient(server);

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task AuthorizeAsync_BankReturns400_ReturnsUnavailable()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error_message\":\"Not all required properties were sent\"}"));
        var client = CreateClient(server);

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task AuthorizeAsync_BankReturns500_ReturnsUnavailable()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));
        var client = CreateClient(server);

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Unavailable, result.Status);
    }

    // ── Transport failures → Unavailable ─────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_BankTimesOut_ReturnsUnavailable()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(10)));
        var client = CreateClient(server, timeout: TimeSpan.FromMilliseconds(200));

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task AuthorizeAsync_BankReturnsInvalidJson_ReturnsUnavailable()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("not-valid-json"));
        var client = CreateClient(server);

        // Act
        var result = await client.AuthorizeAsync(SampleRequest);

        // Assert
        Assert.Equal(BankResponseStatus.Unavailable, result.Status);
    }

    // ── Wire shape ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_SendsSnakeCaseBodyWithExpiryDate()
    {
        // Arrange
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/payments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"authorized\":true,\"authorization_code\":\"xyz\"}"));

        var client = CreateClient(server);
        var request = new BankAuthorizationRequest
        {
            CardNumber = "2222405343248877",
            ExpiryDate = "04/2027",
            Currency = "GBP",
            Amount = 1050,
            Cvv = "123"
        };

        // Act
        await client.AuthorizeAsync(request);

        // Assert — verify the exact wire shape the bank received
        var logEntry = Assert.Single(server.LogEntries);
        using var doc = JsonDocument.Parse(logEntry.RequestMessage!.Body!);
        var root = doc.RootElement;

        Assert.Equal("2222405343248877", root.GetProperty("card_number").GetString());
        Assert.Equal("04/2027", root.GetProperty("expiry_date").GetString());
        Assert.Equal("GBP", root.GetProperty("currency").GetString());
        Assert.Equal(1050, root.GetProperty("amount").GetInt32());
        Assert.Equal("123", root.GetProperty("cvv").GetString());
    }
}