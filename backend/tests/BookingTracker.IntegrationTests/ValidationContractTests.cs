using System.Net;
using System.Text.Json;
using BookingTracker.IntegrationTests.Infrastructure;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// The error contract the frontend's `errorMessage` / `validationErrors`
/// helpers are written against.
///
/// This is a WIRE-SHAPE test, not a test of any individual rule: what matters
/// is that a rejection keeps arriving as `{ title, errors: { key: [message] } }`
/// with string keys and string-array values. `errorMessage` resolves an
/// ApiError carrying `errors` to those messages and falls back to `title` only
/// when there are none, so flattening `errors` into a string, renaming it, or
/// returning a bare ProblemDetails would silently reduce every rejection in the
/// product to the two words "Validation failed" - which is exactly the bug the
/// validation-surfacing pass fixed on the client side.
///
/// Note there are TWO producers of this shape and both are pinned below:
/// ExceptionHandlingMiddleware (FluentValidation) and ASP.NET's own model
/// binding, which titles it differently.
/// </summary>
public class ValidationContractTests : ApiTestBase
{
    private async Task<HttpClient> OrganizerClientAsync()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "validation-page"));
        return ClientFor(workspace.Organizer);
    }

    /// <summary>Asserts the parts every consumer of this API relies on.</summary>
    private static void AssertUsableByTheFrontend(JsonElement body)
    {
        Assert.Equal(JsonValueKind.String, body.GetProperty("title").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));

        var errors = body.GetProperty("errors");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);

        foreach (var entry in errors.EnumerateObject())
        {
            // A key that names no field is normal (""/"$"/indexed paths all
            // occur) - what must hold is that the VALUE is always an array of
            // readable strings, since that is what gets shown to a human.
            Assert.Equal(JsonValueKind.Array, entry.Value.ValueKind);
            Assert.True(entry.Value.GetArrayLength() > 0, $"errors['{entry.Name}'] was empty.");
            foreach (var message in entry.Value.EnumerateArray())
            {
                Assert.Equal(JsonValueKind.String, message.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
            }
        }
    }

    // ---- Property-level: a FluentValidation rule against one property -------

    [Fact]
    public async Task APropertyLevelRuleIsKeyedByItsPropertyName()
    {
        var client = await OrganizerClientAsync();

        var response = await client.PutAsync("/api/organizer/availability/schedule",
            RawJson("""{"timeZoneId":"Not/A/Real/Zone","days":[]}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);

        Assert.Equal("Validation failed", body.GetProperty("title").GetString());
        AssertUsableByTheFrontend(body);
        // Verbatim PascalCase: Program.cs sets PropertyNamingPolicy but not
        // DictionaryKeyPolicy, and camelCase never applies to dictionary keys.
        // The client matches case-insensitively, so this is a documented fact
        // rather than something it depends on.
        Assert.True(body.GetProperty("errors").TryGetProperty("TimeZoneId", out _));
    }

    [Fact]
    public async Task TheMessageIsInErrorsRatherThanTheTitle()
    {
        // The whole reason `errorMessage` reads `errors` at all: the title is a
        // fixed string and carries no information about what was wrong.
        var client = await OrganizerClientAsync();

        var response = await client.PutAsync("/api/organizer/availability/schedule",
            RawJson("""{"timeZoneId":"Not/A/Real/Zone","days":[]}"""));

        var body = await ReadJsonAsync(response);
        var messages = body.GetProperty("errors").GetProperty("TimeZoneId");

        Assert.Contains(
            messages.EnumerateArray().Select(m => m.GetString()),
            m => m is not null && m.Length > "Validation failed".Length);
    }

    // ---- Root-level: a rule written against the request itself --------------

    [Fact]
    public async Task ARootLevelRuleProducesAnEmptyStringKey()
    {
        // RuleFor(x => x) has no property name, so FluentValidation keys it "".
        // Pinned because a client that assumed every key names a field would
        // drop this message entirely - which is why `unmapped()` exists.
        var client = await OrganizerClientAsync();
        var date = TestData.NextBookableWeekday();

        // An override whose ranges overlap: the rule is about the request as a
        // whole rather than any single property.
        var response = await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""
            {"date":"{{date:yyyy-MM-dd}}","ranges":[
              {"start":"09:00:00","end":"12:00:00"},
              {"start":"11:00:00","end":"14:00:00"}],"note":null}
            """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertUsableByTheFrontend(body);

        var keys = body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains(keys, k => k.Length == 0 || k == "Ranges");
    }

    // ---- ASP.NET's own model binding: the second producer -------------------

    [Fact]
    public async Task MalformedJsonIsA400InAShapeTheClientCanStillRead()
    {
        // Produced by model binding, not by the app's middleware, and titled
        // differently ("One or more validation errors occurred.") - so a client
        // must not key off the title text.
        var client = await OrganizerClientAsync();

        var response = await client.PutAsync("/api/organizer/availability/schedule",
            RawJson("""{"timeZoneId": "UTC", "days": [ }"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertUsableByTheFrontend(body);
    }

    [Fact]
    public async Task AWrongTypeForABoundPropertyIsA400()
    {
        var client = await OrganizerClientAsync();

        var response = await client.PutAsync("/api/organizer/availability/schedule",
            RawJson("""{"timeZoneId": 42, "days": []}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertUsableByTheFrontend(await ReadJsonAsync(response));
    }

    [Fact]
    public async Task AnUnparseableRouteGuidIsA404RatherThanA500()
    {
        // The {pageId:guid} route constraint - no controller code runs at all.
        var client = await OrganizerClientAsync();

        var response = await client.GetAsync("/api/organizer/booking-pages/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- A tracked-event batch: deduplicated messages ----------------------

    [Fact]
    public async Task AnInvalidTrackedEventIsRejectedWithFieldKeyedMessages()
    {
        // The booking wizard reports every keystroke as its own event, so a
        // batch can carry the same broken rule many times over. Only the shape
        // is asserted here; the dedup rule itself is the client's job.
        await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "events-page"));
        var sessionId = await BookingFlow.StartSessionAsync(Client, "events-page");

        var response = await Client.PostAsync($"/api/booking-sessions/{sessionId}/events",
            RawJson("""[{"eventType":"DateSelected","fieldName":null,"newValue":"not-a-date","clientSequenceNumber":1}]"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Validation failed", body.GetProperty("title").GetString());
        AssertUsableByTheFrontend(body);
    }

    [Fact]
    public async Task AnOversizedFieldValueIsA400RatherThanADatabaseError()
    {
        // The rule this exists for: a column limit must never be
        // the thing that rejects input. Before validation was added this reached
        // SQL Server and surfaced as an opaque 500.
        await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "limits-page"));
        var sessionId = await BookingFlow.StartSessionAsync(Client, "limits-page");
        var tooLong = new string('x', 5000);

        var response = await Client.PostAsync($"/api/booking-sessions/{sessionId}/events",
            RawJson($$"""[{"eventType":"FieldChanged","fieldName":"Name","newValue":"{{tooLong}}","clientSequenceNumber":1}]"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertUsableByTheFrontend(await ReadJsonAsync(response));
    }

    // ---- Non-validation errors keep the simpler shape -----------------------

    [Fact]
    public async Task ANotFoundCarriesATitleAndNoErrorsMap()
    {
        // `errorMessage` falls back to the title when there is no `errors` key,
        // so this half of the contract matters just as much.
        var response = await Client.GetAsync("/api/booking-pages/nothing-here");

        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.String, body.GetProperty("title").ValueKind);
        Assert.False(body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task AnUnauthenticatedOrganizerRequestIs401WithNoBodyLeak()
    {
        var response = await Client.GetAsync("/api/organizer/booking-pages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Whatever the body is, it must not be an unhandled exception dump.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BookingTracker.", body, StringComparison.Ordinal);
    }
}
