using BookingTracker.Application.Auth.Commands.Login;
using BookingTracker.Application.Auth.Commands.Logout;
using BookingTracker.Application.Auth.Commands.RefreshAccessToken;
using BookingTracker.Application.Auth.Commands.Register;
using BookingTracker.Application.Auth.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingTracker.Api.Controllers;

/// <summary>
/// Organizer authentication. All endpoints here are public - they issue the
/// credentials that Authorize-protected organizer endpoints elsewhere require.
/// Every action is anonymous and credential/token-guessable, so the whole
/// controller is rate-limited per client IP (see Program.cs's
/// "AuthenticationPolicy").
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("AuthenticationPolicy")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a new organizer account and immediately signs them in.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RegisterCommand(request.Name, request.Email, request.Password), cancellationToken);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        return Ok(result);
    }

    /// <summary>Exchanges a still-valid refresh token for a new access/refresh token pair (rotation).</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResultDto>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RefreshAccessTokenCommand(request.RefreshToken), cancellationToken);
        return Ok(result);
    }

    /// <summary>Revokes a refresh token. Idempotent - always succeeds.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return NoContent();
    }
}

public record RegisterRequest(string Name, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
