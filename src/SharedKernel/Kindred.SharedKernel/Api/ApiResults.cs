using System.Text.Json;
using System.Text.Json.Serialization;
using Kindred.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kindred.SharedKernel.Api;

/// <summary>The only error shape an API returns. Success responses are raw DTOs.</summary>
public sealed record ApiError(string Code, string Message);

public static class ApiResults
{
    /// <summary>Success → 200 with the raw DTO; failure → <see cref="ApiError"/> with 400/404/409.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    /// <summary>Success → 201 with the raw DTO and a Location header.</summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? Results.Created(location(result.Value), result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Json(new ApiError(error.Code, error.Message), statusCode: error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });

    /// <summary>camelCase properties, enums as SCREAMING_SNAKE_CASE strings.</summary>
    public static IServiceCollection AddKindredJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)));
}
