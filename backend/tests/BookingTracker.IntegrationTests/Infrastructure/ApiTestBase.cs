using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BookingTracker.Domain.Entities;
using BookingTracker.Infrastructure.Persistence;

namespace BookingTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Base for every integration test class: one factory (and therefore one
/// database and one rate-limiter partition) per class, disposed with it.
///
/// Inherit rather than share a collection fixture, so a test class can seed
/// whatever starting state it likes without coordinating with any other.
/// </summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    protected BookingTrackerApiFactory Factory { get; }

    protected ApiTestBase() => Factory = CreateFactory();

    /// <summary>
    /// The host this class's tests run against. Virtual for
    /// BookingTracker.SqlServerTests, which swaps in a factory backed by a real
    /// SQL Server database; everything else about the base - the clients, the
    /// JSON options, the db helpers - is shared.
    /// </summary>
    protected virtual BookingTrackerApiFactory CreateFactory() => new();

    /// <summary>Anonymous by default - exactly what a public booking visitor is.</summary>
    protected HttpClient Client { get; private set; } = default!;

    /// <summary>The same JSON options the API itself uses, so tests read responses the way a client would.</summary>
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public virtual Task InitializeAsync()
    {
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A client whose every request carries this organizer's bearer token.</summary>
    protected HttpClient ClientFor(Organizer organizer)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.AccessTokenFor(organizer));
        return client;
    }

    protected HttpClient ClientWithRawToken(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected Task<T> WithDbAsync<T>(Func<BookingTrackerDbContext, Task<T>> work) => Factory.WithDbAsync(work);

    protected Task WithDbAsync(Func<BookingTrackerDbContext, Task> work) => Factory.WithDbAsync(work);

    /// <summary>
    /// Posts a raw JSON string rather than an object, for the tests that are
    /// specifically about what the wire format does to model binding - an
    /// invalid enum spelling, a malformed body, a missing property. Serializing
    /// a typed object would let the test's own C# types decide what reaches the
    /// binder, which is the boundary under test.
    /// </summary>
    protected static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json))
           ?? throw new InvalidOperationException($"Expected a {typeof(T).Name} body, got: {await response.Content.ReadAsStringAsync()}");
}
