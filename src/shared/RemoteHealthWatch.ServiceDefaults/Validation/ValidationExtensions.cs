using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace RemoteHealthWatch.ServiceDefaults.Validation;

/// <summary>Runs any registered FluentValidation validator for action arguments and returns a 400 ValidationProblem.</summary>
public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            context.Result = new ObjectResult(new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
            return;
        }
        await next();
    }
}

public static class ValidationExtensions
{
    public static IMvcBuilder AddRemoteHealthWatchControllers<TMarker>(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<TMarker>();
        return services.AddControllers(o => o.Filters.Add<FluentValidationFilter>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });
    }
}
