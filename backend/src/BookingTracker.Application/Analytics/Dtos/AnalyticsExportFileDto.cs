namespace BookingTracker.Application.Analytics.Dtos;

/// <summary>
/// A rendered export, ready to be handed to the browser. The Application layer
/// decides the bytes, the content type AND the file name - the controller only
/// forwards them, so nothing about the export format leaks into the Api layer
/// and the file name is covered by the same handler tests as the content.
/// </summary>
public record AnalyticsExportFileDto(string FileName, string ContentType, byte[] Content);
