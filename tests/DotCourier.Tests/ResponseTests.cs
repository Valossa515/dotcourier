using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DotCourier.Tests;

public class ResponseTests
{
    [Fact]
    public void Values_and_errors_convert_implicitly()
    {
        Response<int> ok = 5;
        Response<int> notFound = Response.NotFound("missing");

        Assert.True(ok.IsSuccess);
        Assert.Equal(5, ok.Data);
        Assert.False(notFound.IsSuccess);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public void GetDataOrThrow_throws_with_the_status_code()
    {
        Response<int> error = Response.Conflict("taken");

        var ex = Assert.Throws<ResponseException>(() => error.GetDataOrThrow());

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("taken", ex.Message);
    }

    [Fact]
    public void ToString_does_not_print_data()
    {
        var response = Response.Success("secret-token");

        Assert.DoesNotContain("secret-token", response.ToString());
    }

    [Fact]
    public void Serializes_with_the_spring_courier_shape_and_round_trips()
    {
        var response = Response.Success(new Item(1, "book"), 201);

        var json = JsonSerializer.Serialize(response);
        var back = JsonSerializer.Deserialize<Response<Item>>(json)!;

        Assert.Equal("""{"data":{"Id":1,"Name":"book"},"error":null,"success":true,"statusCode":201}""", json);
        Assert.Equal(response, back);
    }

    [Fact]
    public void ToResult_carries_the_status_code()
    {
        Response<int> error = Response.BadRequest("bad");

        var result = error.ToResult();

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Same(error, Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    [Fact]
    public void ToResult_without_body_returns_204_on_success()
    {
        Assert.IsType<NoContent>(Response.Success().ToResult(includeBody: false));
        Assert.IsType<NoContentResult>(Response.Success().ToActionResult(includeBody: false));
    }

    [Fact]
    public void ToActionResult_carries_the_status_code()
    {
        var result = Assert.IsType<ObjectResult>(Response.Success("x", 202).ToActionResult());

        Assert.Equal(202, result.StatusCode);
    }

    public sealed record Item(int Id, string Name);
}
