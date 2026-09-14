namespace BookingTracker.Application.Auth.Dtos;

public record AuthResultDto(
    Guid OrganizerId,
    string Name,
    string Email,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);
