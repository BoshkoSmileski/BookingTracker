using System.Net.Http.Json;
using System.Text.Json;

namespace BookingTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Drives the public booking wizard the way the browser does - start a session,
/// append tracked events, submit - entirely over HTTP.
///
/// Every step goes through a real endpoint on purpose. Seeding a
/// ready-to-submit session straight into the database would skip precisely the
/// model binding, validation and event-sourcing path this layer exists to
/// cover, and would make a green suite compatible with a booking flow that no
/// client could actually complete.
/// </summary>
public static class BookingFlow
{
    /// <summary>Mirrors what useBookingSessionTracker sends for one tracked interaction.</summary>
    public sealed record ClientEvent(string EventType, string? FieldName, string? NewValue, int ClientSequenceNumber);

    public static async Task<Guid> StartSessionAsync(HttpClient client, string slug)
    {
        var response = await client.PostAsync($"/api/booking-pages/{slug}/sessions", content: null);
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return body.GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> AppendEventsAsync(
        HttpClient client, Guid sessionId, params ClientEvent[] events)
        => client.PostAsJsonAsync($"/api/booking-sessions/{sessionId}/events", events);

    /// <summary>
    /// The four fields a booking cannot be submitted without, plus any extra
    /// events (custom answers, idle transitions) the caller wants interleaved -
    /// each as its own FieldChanged, exactly as the wizard reports keystrokes.
    /// </summary>
    public static async Task FillAsync(
        HttpClient client,
        Guid sessionId,
        DateOnly date,
        TimeOnly time,
        string name = "Jane Doe",
        string email = "jane@example.com",
        IEnumerable<ClientEvent>? extraEvents = null)
    {
        var sequence = 1;
        var events = new List<ClientEvent>
        {
            new("FieldChanged", "Name", name, sequence++),
            new("FieldChanged", "Email", email, sequence++),
            new("DateSelected", null, date.ToString("yyyy-MM-dd"), sequence++),
            new("TimeSelected", null, time.ToString("HH:mm"), sequence++),
        };

        foreach (var extra in extraEvents ?? [])
        {
            events.Add(extra with { ClientSequenceNumber = sequence++ });
        }

        var response = await AppendEventsAsync(client, sessionId, [.. events]);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The submit call itself, left un-asserted so a test can inspect a rejection.</summary>
    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid sessionId, int clientSequenceNumber = 99)
        => client.PostAsJsonAsync($"/api/booking-sessions/{sessionId}/submit", new { clientSequenceNumber });

    /// <summary>Start to confirmed booking in one call, for tests whose subject is what happens afterwards.</summary>
    public static async Task<JsonElement> BookAsync(
        HttpClient client,
        string slug,
        DateOnly date,
        TimeOnly time,
        string name = "Jane Doe",
        string email = "jane@example.com")
    {
        var sessionId = await StartSessionAsync(client, slug);
        await FillAsync(client, sessionId, date, time, name, email);

        var response = await SubmitAsync(client, sessionId);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The first slot the API offers on <paramref name="date"/>, as the wizard would read it.</summary>
    public static async Task<TimeOnly> FirstAvailableTimeAsync(HttpClient client, string slug, DateOnly date)
    {
        var slots = await GetSlotsAsync(client, slug, date, date);
        Assert.True(slots.GetArrayLength() > 0, $"Expected at least one available slot on {date:yyyy-MM-dd}.");
        return TimeOnly.Parse(slots[0].GetProperty("localStartTime").GetString()!);
    }

    public static async Task<JsonElement> GetSlotsAsync(HttpClient client, string slug, DateOnly from, DateOnly to)
    {
        var response = await client.GetAsync(
            $"/api/booking-pages/{slug}/slots?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
    }
}
