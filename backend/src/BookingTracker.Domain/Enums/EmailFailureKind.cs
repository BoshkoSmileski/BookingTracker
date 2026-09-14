namespace BookingTracker.Domain.Enums;

/// <summary>
/// Whether a failed send attempt is worth trying again. The queue's retry
/// budget only means something if it is spent on failures that could plausibly
/// succeed next time - a rejected credential cannot, so retrying one burns the
/// whole budget (roughly a quarter of an hour of backoff) to reach exactly the
/// same outcome, and leaves the email unrecoverable at the end of it.
///
/// The *classification* is made by the caller, because deciding what a given
/// failure means requires knowing the transport (SMTP reply codes today - see
/// SmtpFailureClassifier in Infrastructure). What HAPPENS as a result stays in
/// EmailNotification.RecordFailedAttempt, so retry policy still lives in
/// exactly one place.
///
/// Not persisted: this describes one attempt, not the notification. The
/// outcome it produces is already visible in Status/AttemptCount/LastError.
/// </summary>
public enum EmailFailureKind
{
    /// <summary>
    /// Might succeed on a later attempt - a dropped connection, a timeout, a
    /// 4xx "try again later" from the server. Retried with the existing
    /// exponential backoff until MaxAttempts.
    ///
    /// Deliberately the default for anything unrecognised: assuming a failure
    /// is retryable preserves the behaviour this queue has always had, whereas
    /// assuming it is permanent would silently stop retrying failures that
    /// previously recovered on their own.
    /// </summary>
    Transient = 0,

    /// <summary>
    /// Cannot succeed by resending this message as configured - a rejected or
    /// missing credential, or a server that refuses to accept mail from this
    /// session at all. Fails the notification immediately rather than
    /// consuming the retry budget.
    /// </summary>
    Permanent = 1
}
