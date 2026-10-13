using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Domain;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Persistence;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

/// <summary>
/// Exposes the payment processing and retrieval endpoints.
/// All business logic lives in <see cref="PaymentService"/>; this controller
/// is responsible only for HTTP concerns: routing, status codes, and response mapping.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _paymentService;
    private readonly IPaymentsRepository _paymentsRepository;

    public PaymentsController(PaymentService paymentService, IPaymentsRepository paymentsRepository)
    {
        _paymentService = paymentService;
        _paymentsRepository = paymentsRepository;
    }

    /// <summary>
    /// Processes a new payment request against the acquiring bank.
    /// Returns 201 Created for both Authorized and Declined outcomes because both
    /// represent a completed bank decision and produce a stored payment record that
    /// the client can retrieve via the Location header.
    /// Returns 400 with payment_status "Rejected" if the request fails validation
    /// (the bank is never called in this case).
    /// Returns 502 if the acquiring bank is unreachable or returns an error.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> ProcessPayment(
        PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _paymentService.ProcessAsync(request, cancellationToken);

        // Map the typed outcome to the correct HTTP response.
        // Using a switch expression here rather than if/else keeps the status-code
        // mapping visible in one place and makes it easy to add new outcomes.
        return result.Outcome switch
        {
            PaymentOutcome.Rejected => RejectedProblem(result.Errors),
            PaymentOutcome.BankUnavailable => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The acquiring bank could not be reached."),
            _ => CreatedAtAction(
                nameof(GetPayment),
                new { id = result.Payment!.Id },
                ToPostResponse(result.Payment!))
        };
    }

    /// <summary>
    /// Retrieves a previously processed payment by its unique identifier.
    /// Returns 404 if no payment with the given id exists.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPaymentResponse>> GetPayment(Guid id)
    {
        var payment = await _paymentsRepository.GetAsync(id);

        return payment is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Payment not found.")
            : Ok(ToGetResponse(payment));
    }

    // Builds a 400 ProblemDetails response for rejected payments.
    // The payment_status extension field is required by the API contract so clients
    // can distinguish a rejected payment from other 400 errors (e.g. malformed JSON).
    private ActionResult RejectedProblem(IReadOnlyList<string> errors)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
        };
        problem.Extensions["payment_status"] = "Rejected";
        problem.Extensions["errors"] = errors;
        return BadRequest(problem);
    }

    // PostPaymentResponse and GetPaymentResponse intentionally have the same shape today.
    // Keeping them as separate types allows them to diverge (e.g. adding authorization_code
    // to the POST response only) without a breaking change to the GET contract.
    private static PostPaymentResponse ToPostResponse(Payment payment) => new()
    {
        Id = payment.Id,
        Status = payment.Status,
        CardNumberLastFour = payment.CardNumberLastFour,
        ExpiryMonth = payment.ExpiryMonth,
        ExpiryYear = payment.ExpiryYear,
        Currency = payment.Currency,
        Amount = payment.Amount
    };

    private static GetPaymentResponse ToGetResponse(Payment payment) => new()
    {
        Id = payment.Id,
        Status = payment.Status,
        CardNumberLastFour = payment.CardNumberLastFour,
        ExpiryMonth = payment.ExpiryMonth,
        ExpiryYear = payment.ExpiryYear,
        Currency = payment.Currency,
        Amount = payment.Amount
    };
}