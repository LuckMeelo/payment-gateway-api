using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests;

// These tests require the bank simulator to be running (docker compose up -d bank_simulator).
// They are excluded from the normal test loop via --filter "Category!=E2E".
[Trait("Category", "E2E")]
public class PaymentGatewayE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly string BankBaseUrl =
        Environment.GetEnvironmentVariable("E2E_BANK_BASE_URL") ?? "http://localhost:8080";

    // Probe once per test run; the Lazy ensures we don't hammer the admin port per-test.
    private static readonly Lazy<bool> SimulatorReady = new(() =>
    {
        EnsureSimulatorRunning(BankBaseUrl);
        return true;
    });

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentGatewayE2ETests(WebApplicationFactory<Program> factory)
    {
        _ = SimulatorReady.Value;
        // Allow E2E_BANK_BASE_URL to redirect outbound bank calls (useful in CI if ports differ).
        _factory = factory.WithWebHostBuilder(b => b.UseSetting("Bank:BaseUrl", BankBaseUrl));
    }

    private static void EnsureSimulatorRunning(string bankUrl)
    {
        // Poll Mountebank admin (port 2525) so tests fail fast with a clear message instead of
        // emitting confusing socket errors when the simulator is not up.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                http.GetAsync("http://localhost:2525").GetAwaiter().GetResult();
                return;
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
        throw new InvalidOperationException(
            $"Bank simulator not reachable at {bankUrl}. " +
            "Start it with `docker compose up -d bank_simulator` before running E2E tests.");
    }

    private static PostPaymentRequest ValidRequest(string cardNumber) => new()
    {
        CardNumber = cardNumber,
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    // Card ending 1 → bank authorizes → 201 Authorized; then GET confirms the record was stored.

    [Fact]
    public async Task PostPayment_AuthorizedCard_Returns201AuthorizedAndIsRetrievable()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act — POST
        var postResponse = await client.PostAsJsonAsync(
            "/api/payments", ValidRequest("2222405343248871"), SnakeCaseOptions);
        var postBody = await postResponse.Content
            .ReadFromJsonAsync<PostPaymentResponse>(SnakeCaseOptions);

        // Assert POST
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.NotNull(postBody);
        Assert.Equal(PaymentStatus.Authorized, postBody.Status);
        Assert.Equal("8871", postBody.CardNumberLastFour);
        Assert.Equal(100, postBody.Amount);
        Assert.Equal("GBP", postBody.Currency);

        // Act — GET the stored payment by id
        var getResponse = await client.GetAsync($"/api/payments/{postBody.Id}");
        var getBody = await getResponse.Content
            .ReadFromJsonAsync<GetPaymentResponse>(SnakeCaseOptions);

        // Assert GET round-trip
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getBody);
        Assert.Equal(postBody.Id, getBody.Id);
        Assert.Equal(PaymentStatus.Authorized, getBody.Status);
        Assert.Equal("8871", getBody.CardNumberLastFour);
    }

    // Card ending 2 → bank declines → 201 Declined.

    [Fact]
    public async Task PostPayment_DeclinedCard_Returns201Declined()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/payments", ValidRequest("2222405343248872"), SnakeCaseOptions);
        var body = await response.Content
            .ReadFromJsonAsync<PostPaymentResponse>(SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Declined, body.Status);
        Assert.Equal("8872", body.CardNumberLastFour);
    }

    // Card ending 0 → bank returns 503 → gateway maps to 502 Bad Gateway.

    [Fact]
    public async Task PostPayment_BankUnavailable_Returns502()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/payments", ValidRequest("2222405343248870"), SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}