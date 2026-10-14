using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DotCourier.Tests;

public sealed record Ship(int OrderId) : ICommand<Response<string>>;

public sealed class ShipHandler : ICommandHandler<Ship, Response<string>>
{
    public Task<Response<string>> Handle(Ship command, CancellationToken ct) =>
        Task.FromResult<Response<string>>(command.OrderId switch
        {
            0 => Response.NotFound("Order not found", code: "ORD-404"),
            1 => Response.Unauthorized("Login required", code: "AUTH-001"),
            2 => Response.Forbidden("Not your order"),
            3 => Response.NoContent(),
            _ => Response.Created($"shipment-{command.OrderId}"),
        });
}

public sealed record Reserve(int Quantity) : ICommand<string>;

public sealed class ReserveHandler : ICommandHandler<Reserve, string>
{
    public Task<string> Handle(Reserve command, CancellationToken ct) =>
        throw new CourierException("Out of stock", 409) { Code = "STK-001" };
}

public class ResponseHelpersTests
{
    [Theory]
    [InlineData(0, 404, "ORD-404")]
    [InlineData(1, 401, "AUTH-001")]
    [InlineData(2, 403, null)]
    [InlineData(3, 204, null)]
    [InlineData(9, 201, null)]
    public async Task Handlers_pick_status_and_code_with_helpers(int orderId, int status, string? code)
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Ship(orderId));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, response.ErrorCode);
        Assert.Equal(status < 300, response.IsSuccess);
    }

    [Fact]
    public void Created_carries_the_data()
    {
        var response = Response.Created(new { Id = 1 });

        Assert.Equal(201, response.StatusCode);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public async Task CourierException_code_reaches_the_response()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Reserve(10));

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("STK-001", response.ErrorCode);
    }

    [Fact]
    public void ErrorCode_is_serialized_only_when_present()
    {
        Response<int> withCode = Response.NotFound("missing", code: "X-1");
        Response<int> withoutCode = Response.NotFound("missing");

        Assert.Contains("\"errorCode\":\"X-1\"", JsonSerializer.Serialize(withCode));
        Assert.DoesNotContain("errorCode", JsonSerializer.Serialize(withoutCode));
        Assert.DoesNotContain("\"Code\"", JsonSerializer.Serialize(new ValidationError("Name", "required")));
    }

    [Fact]
    public void GetDataOrThrow_keeps_the_code()
    {
        Response<int> error = Response.Conflict("taken", code: "DUP-1");

        var ex = Assert.Throws<ResponseException>(() => error.GetDataOrThrow());

        Assert.Equal("DUP-1", ex.Code);
    }

    [Fact]
    public void Unit_serializes_as_null_and_round_trips()
    {
        Response<Unit> ok = Response.Success();
        Response<Unit> missing = Response.NotFound("gone");

        var okJson = JsonSerializer.Serialize(ok);
        Assert.Contains("\"data\":null", okJson);
        Assert.Contains("\"data\":null", JsonSerializer.Serialize(missing));
        Assert.Equal(ok, JsonSerializer.Deserialize<Response<Unit>>(okJson));
    }

    [Fact]
    public void NoContent_becomes_a_bodiless_204_result()
    {
        Response<string> response = Response.NoContent();

        Assert.IsType<NoContent>(response.ToResult());
        Assert.Equal(201, Assert.IsAssignableFrom<IStatusCodeHttpResult>(Response.Created("x").ToResult()).StatusCode);
    }
}
