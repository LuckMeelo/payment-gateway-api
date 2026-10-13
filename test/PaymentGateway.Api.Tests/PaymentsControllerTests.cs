using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    // Must match the API's serialization settings: snake_case properties + string enums.
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public PaymentsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(IAcquiringBankClient bankClient)
    {
        return _factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAcquiringBankClient>();
                services.AddSingleton(bankClient);
            }))
            .CreateClient();
    }

    private static IAcquiringBankClient BankReturning(BankResponseStatus status, string? code = null)
    {
        var stub = Substitute.For<IAcquiringBankClient>();
        stub.AuthorizeAsync(Arg.Any<BankAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BankAuthorizationResult(status, code)));
        return stub;
    }

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    //POST 201 

    [Fact]
    public async Task ProcessPayment_AuthorizedCard_Returns201WithLocationAndBody()
    {
        // Arrange
        var client = CreateClient(BankReturning(BankResponseStatus.Authorized, "auth-123"));

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest(), SnakeCaseOptions);
        var body = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Authorized, body.Status);
        Assert.Equal("8877", body.CardNumberLastFour);
        Assert.Equal(100, body.Amount);
        Assert.Contains(body.Id.ToString(), response.Headers.Location.ToString());
    }

    [Fact]
    public async Task ProcessPayment_DeclinedCard_Returns201Declined()
    {
        // Arrange
        var client = CreateClient(BankReturning(BankResponseStatus.Declined));

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest(), SnakeCaseOptions);
        var body = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Declined, body.Status);
    }

    // POST 400 

    [Fact]
    public async Task ProcessPayment_InvalidRequest_Returns400RejectedAndDoesNotCallBank()
    {
        // Arrange — all-null body triggers every validation error
        var bankStub = BankReturning(BankResponseStatus.Authorized);
        var client = CreateClient(bankStub);

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", new PostPaymentRequest(), SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await bankStub.DidNotReceive()
            .AuthorizeAsync(Arg.Any<BankAuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    // POST 502 

    [Fact]
    public async Task ProcessPayment_BankUnavailable_Returns502()
    {
        // Arrange
        var client = CreateClient(BankReturning(BankResponseStatus.Unavailable));

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest(), SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    // Round trip: POST then GET

    [Fact]
    public async Task ProcessPayment_ThenGetById_ReturnsStoredPayment()
    {
        // Arrange
        var client = CreateClient(BankReturning(BankResponseStatus.Authorized, "code"));

        // Act — create the payment
        var postResponse = await client.PostAsJsonAsync("/api/payments", ValidRequest(), SnakeCaseOptions);
        var postBody = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(SnakeCaseOptions);

        // Act — retrieve it via the Location header
        var getResponse = await client.GetAsync(postResponse.Headers.Location);
        var getBody = await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>(SnakeCaseOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(postBody);
        Assert.NotNull(getBody);
        Assert.Equal(postBody.Id, getBody.Id);
        Assert.Equal(PaymentStatus.Authorized, getBody.Status);
        Assert.Equal("8877", getBody.CardNumberLastFour);
    }

    // GET 404

    [Fact]
    public async Task GetPayment_UnknownId_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}