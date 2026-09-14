using System.Net;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Exceptions;
using ValidationException = BookingTracker.Application.Common.Exceptions.ValidationException;

namespace BookingTracker.Api.Middleware;

/// <summary>
/// Translates the small set of exception types the Application/Domain layers
/// throw into the appropriate HTTP status codes, so command/query handlers
/// never need to know about HTTP at all.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (AuthenticationException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Unauthorized, ex.Message);
        }
        catch (ForbiddenException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Forbidden, ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Conflict, ex.Message);
        }
        catch (ValidationException ex)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(new { title = "Validation failed", errors = ex.Errors });
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Path}", context.Request.Path);
            await WriteProblemAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;
        return context.Response.WriteAsJsonAsync(new { title = message });
    }
}
