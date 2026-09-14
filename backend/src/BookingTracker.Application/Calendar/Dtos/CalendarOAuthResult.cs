namespace BookingTracker.Application.Calendar.Dtos;

public record CalendarOAuthResult(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, string AccountEmail);
