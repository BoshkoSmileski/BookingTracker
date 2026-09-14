using System.Net;
using System.Text.Json;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// The regression this whole integration layer was built for.
///
/// `AddFormFieldRequest` originally bound `BookingFieldType` directly. Every
/// handler test passed, because a handler test constructs the command with an
/// already-typed enum and never crosses the wire at all. A real HTTP request
/// did not: this API carries enums as STRINGS in both directions (mappings call
/// .ToString()) and registers no JsonStringEnumConverter, so binding the enum
/// demanded a numeric `"type": 0` from clients, and a typo produced a
/// framework-shaped ProblemDetails instead of this API's own error contract.
///
/// The fix is a `string Type` on the request plus Enum.TryParse in the
/// controller. These tests pin that contract from outside the process, so
/// reverting to a bound enum fails here while the handler tests stay green -
/// which is exactly the gap that made the bug shippable.
/// </summary>
public class CustomQuestionEnumBindingTests : ApiTestBase
{
    private async Task<(TestData.Workspace Workspace, HttpClient Client)> ArrangeAsync()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "questions-page"));
        return (workspace, ClientFor(workspace.Organizer));
    }

    [Theory]
    [InlineData("ShortText", BookingFieldType.ShortText)]
    [InlineData("LongText", BookingFieldType.LongText)]
    public async Task AValidEnumName_IsAcceptedAndPersisted(string wireValue, BookingFieldType expected)
    {
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson($$"""{"label":"Company","type":"{{wireValue}}","isRequired":true}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // FormFields is an owned collection on BookingPage, so it comes back
        // with the aggregate rather than from a DbSet of its own.
        var page = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == workspace.Page.Id));
        var stored = Assert.Single(page.FormFields);
        Assert.Equal(expected, stored.Type);
        Assert.Equal("Company", stored.Label);
        Assert.True(stored.IsRequired);
    }

    [Fact]
    public async Task TheExactPayloadTheFrontendSends_Succeeds()
    {
        // Byte-for-byte what lib/api.ts posts: camelCase properties, the enum as
        // its name. If binding ever reverts to the enum type, this is a 400.
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"What would you like to discuss?","type":"LongText","isRequired":false}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheResponseCarriesTheTypeBackAsAStringName_NotANumber()
    {
        // The other half of the contract: types.ts mirrors this as a string
        // union, so a numeric value here would break the editor even though the
        // write succeeded.
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"ShortText","isRequired":true}"""));

        var page = await ReadJsonAsync(response);
        var field = page.GetProperty("formFields")[0];
        Assert.Equal(JsonValueKind.String, field.GetProperty("type").ValueKind);
        Assert.Equal("ShortText", field.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ANumericEnumValue_IsRejected()
    {
        // Precisely what the buggy version REQUIRED. Pinned so that a revert is
        // caught from both directions, not just by the string case passing.
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":0,"isRequired":true}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Dropdown")]   // a plausible future type that does not exist yet
    [InlineData("Short Text")] // the label rather than the enum name
    [InlineData("Number")]
    [InlineData("")]
    public async Task AnUnknownEnumValue_IsA400FromThisApi(string wireValue)
    {
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson($$"""{"label":"Company","type":"{{wireValue}}","isRequired":true}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownEnumValue_ExplainsWhatWasExpected()
    {
        // The controller's own message, not a framework one - which is the
        // difference the string-plus-TryParse shape buys.
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"Dropdown","isRequired":true}"""));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Dropdown", body);
        Assert.Contains("ShortText", body);
        Assert.Contains("LongText", body);
    }

    [Fact]
    public async Task ARejectedFieldIsNotPersisted()
    {
        var (workspace, client) = await ArrangeAsync();

        await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"Dropdown","isRequired":true}"""));

        var page = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == workspace.Page.Id));
        Assert.Empty(page.FormFields);
    }

    [Fact]
    public async Task LowercaseEnumNames_AreAcceptedCaseInsensitively()
    {
        // The controller parses with ignoreCase: true, so this is the documented
        // behaviour rather than an accident - pinned so tightening it becomes a
        // deliberate decision.
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"shorttext","isRequired":true}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- The answer side: a custom answer through the real booking wizard ----

    [Fact]
    public async Task AGuestsAnswerReachesTheDatabaseThroughTheRealWizard()
    {
        // The full boundary: the organizer creates a question over HTTP, a guest
        // answers it over HTTP as an ordinary FieldChanged event named
        // custom:{fieldId}, and the answer is persisted against the booking.
        var (workspace, organizerClient) = await ArrangeAsync();

        var created = await organizerClient.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"ShortText","isRequired":true}"""));
        var fieldId = (await ReadJsonAsync(created)).GetProperty("formFields")[0].GetProperty("id").GetGuid();

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, "questions-page", date);

        var sessionId = await BookingFlow.StartSessionAsync(Client, "questions-page");
        await BookingFlow.FillAsync(Client, sessionId, date, time, extraEvents:
        [
            new BookingFlow.ClientEvent("FieldChanged", BookingFieldNames.ForCustomField(fieldId), "Acme Ltd", 0),
        ]);

        var submit = await BookingFlow.SubmitAsync(Client, sessionId);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync(s => s.Id == sessionId));
        var answer = Assert.Single(session.Answers);
        Assert.Equal(fieldId, answer.BookingFormFieldId);
        Assert.Equal("Acme Ltd", answer.Value);
    }

    [Fact]
    public async Task AMissingRequiredAnswer_IsRejectedAtSubmitWithAFieldKeyedError()
    {
        // Requiredness is page policy checked in the submit handler, and the
        // error is keyed custom:{fieldId} so the wizard can point at the input.
        // That key crossing the wire intact is the part only an HTTP test sees.
        var (workspace, organizerClient) = await ArrangeAsync();

        var created = await organizerClient.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"ShortText","isRequired":true}"""));
        var fieldId = (await ReadJsonAsync(created)).GetProperty("formFields")[0].GetProperty("id").GetGuid();

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, "questions-page", date);

        var sessionId = await BookingFlow.StartSessionAsync(Client, "questions-page");
        await BookingFlow.FillAsync(Client, sessionId, date, time);

        var submit = await BookingFlow.SubmitAsync(Client, sessionId);

        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        var body = await ReadJsonAsync(submit);
        Assert.Equal("Validation failed", body.GetProperty("title").GetString());
        Assert.True(
            body.GetProperty("errors").TryGetProperty(BookingFieldNames.ForCustomField(fieldId), out _),
            "The rejection should be keyed by the custom field it is about.");
    }

    [Fact]
    public async Task AnAnswerSurvivesRebuildFromTheEventLog()
    {
        // The event-sourcing claim, asserted through the diagnostic endpoint
        // rather than in-process: the projection and a pure replay must agree.
        var (workspace, organizerClient) = await ArrangeAsync();

        var created = await organizerClient.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"ShortText","isRequired":false}"""));
        var fieldId = (await ReadJsonAsync(created)).GetProperty("formFields")[0].GetProperty("id").GetGuid();

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, "questions-page", date);
        var sessionId = await BookingFlow.StartSessionAsync(Client, "questions-page");
        await BookingFlow.FillAsync(Client, sessionId, date, time, extraEvents:
        [
            new BookingFlow.ClientEvent("FieldChanged", BookingFieldNames.ForCustomField(fieldId), "Acme Ltd", 0),
        ]);

        var projection = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{sessionId}"));
        var rebuilt = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{sessionId}/rebuild"));

        Assert.Equal(
            projection.GetProperty("answers").GetRawText(),
            rebuilt.GetProperty("answers").GetRawText());
        Assert.Equal("Acme Ltd", rebuilt.GetProperty("answers")[0].GetProperty("value").GetString());
    }
}
