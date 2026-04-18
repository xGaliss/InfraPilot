using InfraPilot.Central.Application;
using Microsoft.Extensions.Options;

namespace InfraPilot.Central.Api;

public sealed class OperatorApiKeyEndpointFilter : IEndpointFilter
{
    private readonly CentralOptions _options;

    public OperatorApiKeyEndpointFilter(IOptions<CentralOptions> options)
    {
        _options = options.Value;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var operatorKey = request.Headers["x-operator-key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(_options.OperatorApiKey)
            || !string.Equals(operatorKey, _options.OperatorApiKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
