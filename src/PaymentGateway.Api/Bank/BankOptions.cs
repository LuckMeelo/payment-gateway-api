using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Bank;

public sealed class BankOptions
{
    public const string SectionName = "Bank";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 5;
}