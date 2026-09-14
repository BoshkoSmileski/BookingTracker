using BookingTracker.IntegrationTests.Infrastructure;

namespace BookingTracker.SqlServerTests.Infrastructure;

/// <summary>
/// Base for every test in this assembly: the shared integration-test base, with
/// the host pointed at the real SQL Server database the collection fixture
/// created.
///
/// Because one database is shared by the whole assembly, a test must not assume
/// it is alone in it. <see cref="Unique"/> is how each test gets its own
/// organizer, page and email address - the same isolation-by-construction the
/// browser suite uses, and the reason there is no cleanup step whose failure
/// could poison a later test.
/// </summary>
[Collection(SqlServerCollection.Name)]
public abstract class SqlServerApiTestBase : ApiTestBase
{
    protected override BookingTrackerApiFactory CreateFactory() => new SqlServerApiFactory();

    /// <summary>A value no other test in this database can collide with.</summary>
    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
