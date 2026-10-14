using System.ComponentModel.DataAnnotations;
using FluentValidation;
using DotCourierValidationError = DotCourier.ValidationError;

namespace DotCourier.Tests.FluentValidation;

public sealed record Signup(string Email, [Range(18, 130)] int Age) : ICommand<string>;

public sealed class SignupValidator : AbstractValidator<Signup>
{
    public SignupValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithErrorCode("EMAIL-01");
    }
}

public sealed class SignupHandler(CallLog log) : ICommandHandler<Signup, string>
{
    public Task<string> Handle(Signup command, CancellationToken ct)
    {
        log.Add("signed up");
        return Task.FromResult("welcome " + command.Email);
    }
}

/// <summary>Validates inside the handler, the way a HandlerBase with ValidateAndThrowAsync does.</summary>
public sealed record Rate(int Stars) : ICommand<string>;

public sealed class RateHandler(CallLog log) : ICommandHandler<Rate, string>
{
    private static readonly InlineValidator<Rate> Validator = CreateValidator();

    public async Task<string> Handle(Rate command, CancellationToken ct)
    {
        log.Add("rate");
        await Validator.ValidateAndThrowAsync(command, ct);
        return $"{command.Stars} stars";
    }

    private static InlineValidator<Rate> CreateValidator()
    {
        var validator = new InlineValidator<Rate>();
        validator.RuleFor(x => x.Stars).InclusiveBetween(1, 5).WithErrorCode("RATE-01");
        return validator;
    }
}

public class FluentValidationTests
{
    [Fact]
    public async Task FluentValidation_and_DataAnnotations_failures_share_one_400()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddFluentValidation());

        var response = await services.Courier().Dispatch(new Signup("not-an-email", 10));

        Assert.Equal(400, response.StatusCode);
        var errors = response.GetErrorDetails<IReadOnlyList<DotCourierValidationError>>()!;
        Assert.Contains(errors, e => e is { Field: "Email", Code: "EMAIL-01" });
        Assert.Contains(errors, e => e is { Field: "Age", Code: null });
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Valid_requests_reach_the_handler()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddFluentValidation());

        var response = await services.Courier().Dispatch(new Signup("ana@example.com", 30));

        Assert.True(response.IsSuccess);
        Assert.Equal(["signed up"], log.Entries);
    }

    [Fact]
    public async Task Validators_are_ignored_without_AddFluentValidation()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Signup("not-an-email", 30));

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task ValidateAndThrow_inside_a_handler_becomes_a_400()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddFluentValidation());

        var response = await services.Courier().Dispatch(new Rate(9));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("RATE-01", Assert.Single(response.GetErrorDetails<IReadOnlyList<DotCourierValidationError>>()!).Code);
    }

    [Fact]
    public async Task Without_the_package_ValidateAndThrow_inside_a_handler_is_a_500()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Rate(9));

        Assert.Equal(500, response.StatusCode);
    }

    [Fact]
    public async Task Retry_does_not_repeat_an_invalid_request()
    {
        var (services, log) = TestHost.Build(cfg => cfg
            .AddFluentValidation()
            .AddResilience(o =>
            {
                o.Retry.Enabled = true;
                o.Retry.Delay = TimeSpan.Zero;
            }));

        var response = await services.Courier().Dispatch(new Rate(0));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(["rate"], log.Entries);
    }
}
