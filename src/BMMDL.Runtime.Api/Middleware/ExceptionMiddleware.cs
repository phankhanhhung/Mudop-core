namespace BMMDL.Runtime.Api.Middleware;

using System.Net;
using System.Text.Json;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;

/// <summary>
/// Global exception handling middleware.
/// Catches unhandled exceptions and returns consistent error responses.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.TraceIdentifier;

        // Log the exception (include SQL diagnostics for database errors)
        if (exception is Npgsql.PostgresException pgEx && pgEx.Data.Contains("FailingSQL"))
        {
            if (_env.IsDevelopment())
            {
                _logger.LogError(exception,
                    "Database error. CorrelationId: {CorrelationId}, Path: {Path}, SQL: {Sql}",
                    correlationId, context.Request.Path, pgEx.Data["FailingSQL"]);
            }
            else
            {
                _logger.LogError(exception,
                    "Database error. CorrelationId: {CorrelationId}, Path: {Path}, SqlState: {SqlState}",
                    correlationId, context.Request.Path, pgEx.SqlState);
            }
        }
        else
        {
            _logger.LogError(exception, 
                "Unhandled exception. CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}",
                correlationId, context.Request.Path, context.Request.Method);
        }

        // Determine status code and error message based on exception type
        // SECURITY: Never expose SQL statements, database internals, or raw exception messages
        // from database exceptions to clients. Log full details server-side only.
        var (statusCode, errorCode, message, target) = exception switch
        {
            EntityNotExposedByServiceException e => (HttpStatusCode.Forbidden, ODataConstants.ErrorCodes.EntityNotExposed, e.Message, e.EntityName),
            EntityNotFoundException e => (HttpStatusCode.NotFound, ODataConstants.ErrorCodes.EntityNotFound, e.Message, e.EntityName),
            TenantNotFoundException e => (HttpStatusCode.NotFound, ODataConstants.ErrorCodes.TenantNotFound, e.Message, (string?)null),
            ValidationException e => (HttpStatusCode.BadRequest, ODataConstants.ErrorCodes.ValidationError, e.Message, (string?)null),
            ArgumentException e => (HttpStatusCode.BadRequest, ODataConstants.ErrorCodes.InvalidArgument, e.Message, (string?)null),
            UnauthorizedAccessException e => (HttpStatusCode.Forbidden, ODataConstants.ErrorCodes.Forbidden, e.Message, (string?)null),
            OperationCanceledException => (HttpStatusCode.RequestTimeout, ODataConstants.ErrorCodes.RequestTimeout, "Request was cancelled", (string?)null),
            Npgsql.PostgresException pe => (HttpStatusCode.BadRequest, ODataConstants.ErrorCodes.DatabaseError,
                _env.IsDevelopment() || _env.EnvironmentName == "Test"
                    ? $"{MapPostgresErrorToClientMessage(pe.SqlState)} [{pe.MessageText}]"
                    : MapPostgresErrorToClientMessage(pe.SqlState),
                (string?)null),
            Npgsql.NpgsqlException => (HttpStatusCode.BadRequest, ODataConstants.ErrorCodes.DatabaseError, "A database error occurred while processing the request", (string?)null),
            _ => (HttpStatusCode.InternalServerError, ODataConstants.ErrorCodes.InternalError, "An unexpected error occurred", (string?)null)
        };

        // Build OData error response
        var errorDetails = new List<ODataErrorDetail>();
        
        // Add validation details if present
        if (exception is ValidationException validationEx && validationEx.Errors != null)
        {
            foreach (var (field, messages) in validationEx.Errors)
            {
                foreach (var msg in messages)
                {
                    errorDetails.Add(new ODataErrorDetail
                    {
                        Code = ODataConstants.ErrorCodes.ValidationFieldError,
                        Message = msg,
                        Target = field
                    });
                }
            }
        }

        var errorResponse = new ODataErrorResponse
        {
            Error = new ODataError
            {
                Code = errorCode,
                Message = message,
                Target = target,
                Details = errorDetails.Count > 0 ? errorDetails : null
            }
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; odata.metadata=minimal";

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, options));
    }

    /// <summary>
    /// Maps PostgreSQL SQL state codes to generic client-safe error messages.
    /// Full error details are logged server-side only.
    /// </summary>
    private static string MapPostgresErrorToClientMessage(string? sqlState)
    {
        return sqlState switch
        {
            "23505" => "The operation failed due to a uniqueness constraint violation",
            "23503" => "The operation failed due to a referential integrity constraint",
            "23502" => "A required field is missing or null",
            "23514" => "A value does not satisfy a check constraint",
            "42P01" => "The requested resource could not be found",
            "42703" => "An invalid field was referenced in the request",
            "22P02" => "A value has an invalid format for its expected type",
            "22003" => "A numeric value is out of the allowed range",
            _ => "A database error occurred while processing the request"
        };
    }
}

/// <summary>
/// Exception thrown when an entity cannot be found.
/// </summary>
public class EntityNotFoundException : Exception
{
    public string EntityName { get; }
    public object? EntityId { get; }

    public EntityNotFoundException(string entityName, object? id = null)
        : base(id != null 
            ? $"Entity '{entityName}' with ID '{id}' not found" 
            : $"Entity '{entityName}' not found")
    {
        EntityName = entityName;
        EntityId = id;
    }
}

/// <summary>
/// Exception thrown when an entity is not exposed by any service.
/// </summary>
public class EntityNotExposedByServiceException : Exception
{
    public string EntityName { get; }

    public EntityNotExposedByServiceException(string entityName)
        : base($"Entity '{entityName}' is not exposed by any service and cannot be accessed directly.")
    {
        EntityName = entityName;
    }
}

/// <summary>
/// Exception thrown when a tenant cannot be found.
/// </summary>
public class TenantNotFoundException : Exception
{
    public Guid TenantId { get; }

    public TenantNotFoundException(Guid tenantId)
        : base($"Tenant with ID '{tenantId}' not found")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Exception thrown when validation fails.
/// </summary>
public class ValidationException : Exception
{
    public IDictionary<string, string[]>? Errors { get; }

    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(string message, IDictionary<string, string[]> errors) 
        : base(message)
    {
        Errors = errors;
    }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred")
    {
        Errors = errors;
    }
}

/// <summary>
/// Extension methods for registering exception middleware.
/// </summary>
public static class ExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionMiddleware>();
    }
}
