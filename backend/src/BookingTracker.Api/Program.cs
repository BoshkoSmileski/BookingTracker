using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using BookingTracker.Api.Common;
using BookingTracker.Api.Middleware;
using BookingTracker.Application;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure;
using BookingTracker.Infrastructure.Auth;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.Infrastructure.Persistence.Seed;
using BookingTracker.Infrastructure.RealTime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendCors";

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Missing required 'Jwt' configuration section.");

// The signing secret is deliberately kept out of appsettings*.json (never
// committed) - Development sources it from `dotnet user-secrets`, Production
// from the JWT__Secret environment variable. Fail fast with a clear message
// rather than let JwtBearer/JwtTokenGenerator fail later with an opaque
// null-reference or "IDX10703: signing key not valid" error.
if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
{
    throw new InvalidOperationException(
        "Missing required 'Jwt:Secret' configuration value. In Development, set it via " +
        "User Secrets: dotnet user-secrets set \"Jwt:Secret\" \"<your-secret>\" " +
        "--project src/BookingTracker.Api. In Production, set the JWT__Secret environment " +
        "variable.");
}

const string OrganizerDashboardHubPath = "/hubs/organizer-dashboard";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Browsers can't attach an Authorization header to a WebSocket/SSE
        // handshake, so the SignalR client sends the access token as a query
        // string param instead (via accessTokenFactory). This bridges it into
        // the standard bearer pipeline, scoped strictly to the dashboard hub
        // path so no other endpoint accepts a token from the query string.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments(OrganizerDashboardHubPath))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

var rateLimitingSettings = builder.Configuration.GetSection(RateLimitingSettings.SectionName).Get<RateLimitingSettings>()
    ?? new RateLimitingSettings();

// Every anonymous, unauthenticated POST endpoint is rate-limited per client
// IP with a fixed-window limiter - "public-token" pre-dates this pass and
// protects the token-guessing surface (view/cancel/reschedule a booking);
// "AuthenticationPolicy"/"BookingPolicy" cover login/register/refresh/logout
// and the public booking wizard respectively. All three share the same
// rejection contract: HTTP 429, a Retry-After header, and a JSON body in the
// same `{ title }` shape ExceptionHandlingMiddleware uses everywhere else.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "Too many requests. Please try again later." }, cancellationToken);
    };

    options.AddPolicy("public-token", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.AddPolicy("AuthenticationPolicy", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitingSettings.Authentication.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitingSettings.Authentication.WindowSeconds),
            QueueLimit = rateLimitingSettings.Authentication.QueueLimit,
        }));

    options.AddPolicy("BookingPolicy", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitingSettings.Booking.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitingSettings.Booking.WindowSeconds),
            QueueLimit = rateLimitingSettings.Booking.QueueLimit,
        }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials() // required for SignalR's cookie/negotiate handshake
            // Content-Disposition is not a CORS-safelisted response header, so
            // without this the analytics export downloads would reach the browser
            // but the frontend could not read the file name the server chose and
            // would have to invent one.
            .WithExposedHeaders("Content-Disposition");
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BookingTrackerDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await db.Database.MigrateAsync();
    await DevelopmentSeeder.SeedAsync(db, passwordHasher);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<OrganizerDashboardHub>(OrganizerDashboardHubPath);

app.Run();

/// <summary>
/// Top-level statements compile into an *internal* Program class, which
/// WebApplicationFactory&lt;Program&gt; cannot use as a public test host's base
/// type. This marker makes the generated entry point public - the standard,
/// documented way to make an app testable in-process.
///
/// It is the entire production-side cost of the integration-test layer: no
/// composition changes, no test-only branches in this file, and nothing here
/// behaves differently when the app runs for real. Everything those tests need
/// to substitute (database, email, calendar, background services) is overridden
/// from the test host's own ConfigureServices, never from here.
/// </summary>
public partial class Program;
