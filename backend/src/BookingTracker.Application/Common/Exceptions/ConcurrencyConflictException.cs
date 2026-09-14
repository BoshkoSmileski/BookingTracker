namespace BookingTracker.Application.Common.Exceptions;

/// <summary>
/// The database rolled a transaction back because it lost a race with another
/// concurrent transaction - on SQL Server, a deadlock victim.
///
/// It says one thing only: <em>this attempt did not happen, and the reason was
/// contention rather than anything wrong with the request</em>. It deliberately
/// does NOT say what the contention was about, because the persistence layer
/// cannot know - the same split SmtpFailureClassifier draws for a failed send,
/// where the caller says what the failure IS and the policy of what HAPPENS
/// lives with whoever understands the operation.
///
/// Deciding what a lost race means for the user is therefore the handler's job.
/// A booking submit re-asks the question it was in the middle of - "is that slot
/// taken now?" - and answers with <see cref="ConflictException"/> when it is.
///
/// It is intentionally NOT mapped in ExceptionHandlingMiddleware: an escaped one
/// means a concurrency failure nobody could explain, which is a 500 and belongs
/// in the logs, not a status code invented to make it look handled.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
