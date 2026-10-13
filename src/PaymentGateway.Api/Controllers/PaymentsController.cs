using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Domain;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Persistence;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(PaymentService paymentService, IPaymentsRepository paymentsRepository) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> ProcessPayment(
        PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await paymentService.ProcessAsync(request, cancellationToken);

        return result.Outcome switch
        {
            PaymentOutcome.Rejected => RejectedProblem(result.Errors),
            PaymentOutcome.BankUnavailable => Problem(statusCode: StatusCodes.Status502BadGateway, title: "The acquiring bank could not be reached."),
            _ => CreatedAtAction(nameof(GetPayment), new { id = result.Payment!.Id }, ToPostResponse(result.Payment!))
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPaymentResponse>> GetPayment(Guid id)
    {
        var payment = await paymentsRepository.GetAsync(id);

        return payment is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Payment not found.")
            : Ok(ToGetResponse(payment));
    }

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