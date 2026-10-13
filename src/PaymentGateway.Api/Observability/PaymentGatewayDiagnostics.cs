using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway.Api.Observability;

/// <summary>
/// Central holder for all OpenTelemetry instrumentation primitives used by the gateway.
/// Keeping them in one static class ensures a single ActivitySource and Meter instance
/// are shared across the application lifetime, which is required by the OTel SDK.
/// </summary>
public static class PaymentGatewayDiagnostics
{
    /// <summary>
    /// Service name used to identify this application in traces, metrics, and log correlation.
    /// Must match the name passed to AddService in Program.cs and to AddSource/AddMeter.
    /// </summary>
    public const string ServiceName = "PaymentGateway";

    /// <summary>
    /// ActivitySource used to create custom spans (in the OTel model, a span = an Activity).
    /// The "bank.authorize" span wraps the outbound bank HTTP call so traces show its
    /// latency as a separate child segment.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    // The Meter is private because external code should use the named instruments below,
    // not create new instruments directly against the meter.
    private static readonly Meter Meter = new(ServiceName);

    /// <summary>
    /// Counter incremented once per processed payment, tagged with the outcome status
    /// (Authorized, Declined, Rejected, BankUnavailable). Allows dashboards to show
    /// payment volume broken down by outcome over time.
    /// </summary>
    public static readonly Counter<long> PaymentsProcessed = Meter.CreateCounter<long>(
        "payments.processed",
        unit: "{payment}",
        description: "Payments processed, tagged by outcome status.");

    /// <summary>
    /// Histogram recording the round-trip duration of each acquiring bank call in milliseconds,
    /// tagged with the outcome (Authorized, Declined, Unavailable). Use the p95/p99 buckets
    /// to set SLO thresholds and detect bank latency regressions.
    /// </summary>
    public static readonly Histogram<double> BankRequestDuration = Meter.CreateHistogram<double>(
        "bank.request.duration",
        unit: "ms",
        description: "Acquiring-bank call duration in milliseconds, tagged by outcome.");
}