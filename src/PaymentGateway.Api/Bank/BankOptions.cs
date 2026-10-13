using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Bank;

/// <summary>
/// Configuration for the acquiring bank HTTP client, bound from the "Bank" section
/// of appsettings.json (or environment variables using double-underscore notation,
/// e.g. Bank__BaseUrl=http://bank:8080).
/// ValidateDataAnnotations + ValidateOnStart in Program.cs ensure the app fails
/// fast at startup if required values are missing or invalid.
/// </summary>
public sealed class BankOptions
{
    public const string SectionName = "Bank";

    /// <summary>Base URL of the acquiring bank API (e.g. "http://localhost:8080").</summary>
    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Maximum seconds to wait for a bank response before treating the request
    /// as failed. Must be between 1 and 300 seconds.
    /// </summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 5;
}