using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NSubstitute;

using PaymentGateway.Api.Bank;

namespace PaymentGateway.Api.Tests;

public class HealthChecksTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthChecksTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // /health/live

    [Fact]
    public async Task GetHealthLive_Returns200Healthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // /health/ready

    [Fact]
    public async Task GetHealthReady_Returns200Healthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthReady_IsHealthy_EvenWhenBankUnavailable()
    {
        // Arrange — bank is configured to always throw; readiness must not depend on it
        var failingBank = Substitute.For<IAcquiringBankClient>();
        failingBank
            .AuthorizeAsync(Arg.Any<BankAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns<BankAuthorizationResult>(_ => throw new HttpRequestException("simulated bank outage"));

        var client = _factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAcquiringBankClient>();
                services.AddSingleton(failingBank);
            }))
            .CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert — gateway reports ready even if the acquiring bank is down
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}