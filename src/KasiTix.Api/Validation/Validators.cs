namespace KasiTix.Api.Validation;
using FluentValidation;
using KasiTix.Api.Models;

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
        RuleFor(r => r.Venue).NotEmpty();
        // TODO (R1): StartsAt must be in the future.
        RuleFor(r => r.StartsAt)
            .Must(d => d.ToUniversalTime() > DateTime.UtcNow)
            .WithMessage("StartsAt must be in the future.");
    }
}

public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(r => r.BuyerEmail).NotEmpty().EmailAddress();
        RuleFor(r => r.Lines)
            .NotEmpty()
            .Must(lines => lines is null || lines.Count <= 5)
            .WithMessage("An order can have at most 5 lines.");

        // TODO (R6): every line's Quantity is between 1 and 10. (Hint: RuleForEach.)
        RuleForEach(r => r.Lines)
            .ChildRules(line => line.RuleFor(l => l.Quantity).InclusiveBetween(1, 10));

        // TODO (R6): no TicketTypeId appears twice in Lines.
        RuleFor(r => r.Lines)
            .Must(lines => lines is null || lines.Select(l => l.TicketTypeId).Distinct().Count() == lines.Count)
            .WithMessage("The same ticket type can't appear twice in one order.");
    }
}

// TODO (R4): write CreateTicketTypeRequestValidator yourself, from scratch.
public class CreateTicketTypeRequestValidator : AbstractValidator<CreateTicketTypeRequest>
{
    public CreateTicketTypeRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Price).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Capacity).InclusiveBetween(1, 10_000);
    }
}