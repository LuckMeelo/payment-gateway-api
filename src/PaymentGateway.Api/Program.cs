using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Persistence;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs readable by log aggregators
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

// Stamp every ProblemDetails response with the current trace id for correlation
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails =
    ctx => ctx.ProblemDetails.Extensions["trace_id"] = ctx.HttpContext.TraceIdentifier);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        // Serialize enum values as their string names (e.g. "Authorized") rather than
        // integers. String enums are self-documenting in API responses and safer against
        // enum reordering bugs.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// OpenTelemetry: traces (ASP.NET Core + HttpClient + custom bank span) and
// metrics (built-in http.server + custom payments.processed / bank.request.duration)
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(PaymentGatewayDiagnostics.ServiceName))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(PaymentGatewayDiagnostics.ServiceName)
        .AddConsoleExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(PaymentGatewayDiagnostics.ServiceName)
        .AddConsoleExporter());

// Health checks: live = process up (no checks run); ready = dependencies (bank excluded)
builder.Services.AddHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentRequestValidator>();
builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddScoped<PaymentService>();

builder.Services
    .AddOptions<BankOptions>()
    .Bind(builder.Configuration.GetSection(BankOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IAcquiringBankClient, AcquiringBankClient>((serviceProvider, http) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<BankOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// TLS terminates at the load balancer; skip the app-level redirect inside the container.
// Official .NET images set DOTNET_RUNNING_IN_CONTAINER=true key off that rather than
// the environment, because the container runs as Development (for Swagger).
if (!builder.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}
app.UseAuthorization();
app.MapControllers();

// /health/live    Predicate = _ => false means NO checks run; endpoint returns Healthy
//                 if the process is up, regardless of any registered health checks.
// /health/ready   runs all registered checks (none today, so always Healthy).
//                 The bank is deliberately NOT registered here: a bank outage must not
//                 drain the gateway fleet from the load balancer.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program { }