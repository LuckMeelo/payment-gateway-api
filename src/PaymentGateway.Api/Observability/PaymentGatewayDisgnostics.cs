using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway.Api.Observability;

public static class PaymentGatewayDiagnostics
{
    public const string ServiceName = "PaymentGateway";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    private static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> PaymentsProcessed = Meter.CreateCounter<long>("payments.processed", unit: "{payment}", description: "Payments processed, tagged by outcome status.");

    public static readonly Histogram<double> BankRequestDuration = Meter.CreateHistogram<double>("bank.request.duration", unit: "ms", description: "Acquiring-bank call duration, tagged by outcome.");
}