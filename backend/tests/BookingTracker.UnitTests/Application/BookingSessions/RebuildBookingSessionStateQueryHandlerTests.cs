using BookingTracker.Application.BookingSessions.Queries.RebuildBookingSessionState;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.BookingSessions;

/// <summary>
/// The end-to-end version of the event-sourcing proof: persists a session and
/// its full event log to a real (InMemory) DbContext exactly like the live
/// application would, then drives it through the actual production handler
/// behind GET /api/booking-sessions/{id}/rebuild - not a reimplementation of
/// its query/ordering logic - and confirms the result matches the live
/// projection that was saved. BookingSessionEventSourcingTests covers the
/// domain-level replay logic in isolation; this covers the handler wiring
/// (load, order, Rebuild, map to DTO) around it.
/// </summary>
public class RebuildBookingSessionStateQueryHandlerTests
{
    [Fact]
    public async Task Handle_RebuiltDto_MatchesTheLiveProjectionSavedToTheDatabase()
    {
        // Arrange
        await using var db = InMemoryDbContextFactory.Create();
        var pageId = Guid.NewGuid();
        var live = BookingSessionScenarios.SubmitThenReschedule(pageId, (2026, 10, 1, 9, 0));

        db.BookingSessions.Add(live.Session);
        db.BookingSessionEvents.AddRange(live.Events);
        await db.SaveChangesAsync();

        var handler = new RebuildBookingSessionStateQueryHandler(db);

        // Act
        var rebuiltDto = await handler.Handle(new RebuildBookingSessionStateQuery(live.Session.Id), CancellationToken.None);

        // Assert
        var liveDto = live.Session.ToDto();
        // Compared in two parts because BookingSessionDto is a record carrying a
        // list: record equality is memberwise, and two distinct List instances
        // are never equal however identical their contents. Emptying Answers on
        // both sides keeps the whole-record comparison meaningful, and the list
        // itself is compared element-wise below (BookingSessionAnswerDto is a
        // record of value types, so those elements do compare structurally).
        Assert.Equal(liveDto with { Answers = [] }, rebuiltDto with { Answers = [] });
        Assert.Equal(liveDto.Answers, rebuiltDto.Answers);
    }

    [Fact]
    public async Task Handle_RebuiltDto_CarriesTheCustomFieldAnswersFromTheLogAlone()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var pageId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var live = BookingSessionScenarios.StartFillAnswerAndSubmit(pageId, (companyId, "Acme Ltd"));

        db.BookingSessions.Add(live.Session);
        db.BookingSessionEvents.AddRange(live.Events);
        await db.SaveChangesAsync();

        var handler = new RebuildBookingSessionStateQueryHandler(db);

        var rebuiltDto = await handler.Handle(new RebuildBookingSessionStateQuery(live.Session.Id), CancellationToken.None);

        var answer = Assert.Single(rebuiltDto.Answers);
        Assert.Equal(companyId, answer.FieldId);
        Assert.Equal("Acme Ltd", answer.Value);
        Assert.Equal(live.Session.ToDto().Answers, rebuiltDto.Answers);
    }

    [Fact]
    public async Task Handle_NoEventsForSession_ThrowsNotFound()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var handler = new RebuildBookingSessionStateQueryHandler(db);

        await Assert.ThrowsAsync<BookingTracker.Application.Common.Exceptions.NotFoundException>(
            () => handler.Handle(new RebuildBookingSessionStateQuery(Guid.NewGuid()), CancellationToken.None));
    }
}
