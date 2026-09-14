using System.Net;

namespace BookingTracker.Application.Notifications.Templates;

/// <summary>Rendered output of a template - always both an HTML and a plain-text version, so IEmailSender never has to derive one from the other.</summary>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Every fact a templated booking email needs, gathered once by
/// EmailNotificationService so each EmailTemplates method takes one argument
/// instead of a long, easily-misordered parameter list. Location is nullable
/// and always null today - BookingPage has no location field yet - but the
/// templates already render it when present, so adding one later (e.g. an
/// address or a video-call link) needs no template change.
///
/// Answers holds the guest's replies to the page's custom form fields, already
/// paired with their labels and in the organizer's display order - the pairing
/// happens once in EmailNotificationService, so no template has to know that a
/// BookingSessionAnswer stores a field id rather than a label.
/// </summary>
public sealed record BookingEmailContext(
    string RecipientName,
    string OrganizerName,
    string GuestName,
    string ServiceTitle,
    string DateLabel,
    string TimeLabel,
    string TimeZone,
    int DurationMinutes,
    string? Location,
    string? Notes,
    string BookingReference,
    string ViewUrl,
    string CancelUrl,
    string RescheduleUrl,
    IReadOnlyList<(string Label, string Value)> Answers,
    /// <summary>Human label for the booking's online meeting ("Google Meet"), or null when it has none.</summary>
    string? MeetingLabel = null,
    /// <summary>
    /// The join URL, or null. Null is the normal case, not an error state: an
    /// in-person booking, an organizer with no connected calendar, and a
    /// booking made while Google was unreachable all arrive here as null, and
    /// every template simply omits its Join section rather than rendering an
    /// empty button.
    /// </summary>
    string? MeetingUrl = null,
    /// <summary>
    /// The public booking page this booking was made on, from
    /// <c>IFrontendLinkBuilder.BuildBookingPageUrl</c> - never assembled here.
    /// Only the cancellation email uses it: that is the one notification whose
    /// recipient has no booking left, so "Book another time" is the useful
    /// action where View/Reschedule/Cancel would all lead nowhere.
    /// Omitted (and the action not drawn) when null.
    /// </summary>
    string? BookAgainUrl = null);

/// <summary>
/// Builds the HTML and plain-text content for every outbound email. Pure
/// string composition, no external templating engine - this is
/// Application-layer logic (it only knows domain vocabulary like "booking
/// reference"), completely independent of how IEmailSender eventually
/// transmits the result. All inline styles (not a &lt;style&gt; block)
/// because most email clients strip head-level CSS. IEmailTemplateRenderer
/// is a thin DI seam over these same static methods - the actual template
/// logic lives here, in exactly one place.
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// The product accent - <c>accent-600</c> in the frontend's <c>@theme</c>
    /// palette (index.css), mirrored here by hand exactly as lib/types.ts
    /// mirrors a DTO. Every primary action a recipient can take wears it, so
    /// the journey a guest makes - booking page, then this email, then the
    /// manage page - is one product rather than three.
    ///
    /// Two colours it replaced are worth naming, because both look like
    /// deliberate choices and neither was: <c>#111827</c> on the primary button
    /// (near-black, from before the app had an accent at all) and
    /// <c>#0f766e</c> on the join button (a Tailwind default teal, close enough
    /// to the accent to look like a mistake rather than a second colour).
    /// EmailBrandingTests asserts neither can come back.
    /// </summary>
    private const string AccentColor = "#316e63";

    /// <summary>
    /// Body and value text - <c>gray-900</c>, the app's own. Deliberately NOT
    /// the accent: this is the colour text is set in, and it was previously
    /// named <c>BrandColor</c>, which is what invited a near-black button in
    /// the first place.
    /// </summary>
    private const string TextColor = "#111827";
    private const string MutedColor = "#6b7280";
    private const string BorderColor = "#e5e7eb";

    /// <summary>The guest surface's warm off-white - <c>shell-100</c>, the same page colour PublicShell puts behind the booking sheet.</summary>
    private const string PageBackgroundColor = "#f2f0eb";

    private const string ButtonStyle =
        $"display:inline-block;padding:10px 18px;background:{AccentColor};color:#ffffff;text-decoration:none;border-radius:8px;font-size:14px;font-weight:500;";
    private const string SecondaryButtonStyle =
        $"display:inline-block;padding:10px 18px;background:#ffffff;color:{TextColor};text-decoration:none;border-radius:8px;font-size:14px;font-weight:500;border:1px solid #d1d5db;";

    /// <summary>
    /// The join button is deliberately the loudest thing in the email. Joining
    /// is the one action a recipient takes at the moment the meeting starts,
    /// and by then they are scanning for it rather than reading - so it gets
    /// its own panel above the manage/reschedule/cancel row rather than
    /// becoming a fourth button in it.
    /// </summary>
    private const string JoinButtonStyle =
        $"display:inline-block;padding:12px 22px;background:{AccentColor};color:#ffffff;text-decoration:none;border-radius:8px;font-size:15px;font-weight:600;";

    public static EmailContent BookingConfirmation(BookingEmailContext ctx)
    {
        var subject = $"Confirmed: {ctx.ServiceTitle} with {ctx.OrganizerName}";
        var rows = SummaryRows(ctx, includeAnswers: true);
        var html = WrapLayout("Booking confirmed", $"""
            <p>Hi {Escape(ctx.RecipientName)},</p>
            <p>Your booking is confirmed. Here are the details:</p>
            {SummaryTable(rows)}
            {MeetingSection(ctx)}
            {Buttons(ctx)}
        """);
        var text = WrapText("Booking confirmed",
            $"Hi {ctx.RecipientName},", "Your booking is confirmed. Here are the details:", rows,
            [.. MeetingTextLines(ctx), .. TextLinks(ctx)]);
        return new EmailContent(subject, html, text);
    }

    public static EmailContent OrganizerNewBooking(BookingEmailContext ctx, string guestEmail, string? guestPhone)
    {
        var subject = $"New booking: {ctx.GuestName} - {ctx.ServiceTitle}";
        var rows = SummaryRows(ctx, extraLeading:
        [
            ("Guest", ctx.GuestName),
            ("Email", guestEmail),
            ("Phone", guestPhone ?? "—"),
        ], includeAnswers: true);
        var html = WrapLayout("New booking", $"""
            <p>Hi {Escape(ctx.RecipientName)},</p>
            <p>You have a new booking.</p>
            {SummaryTable(rows)}
            {MeetingSection(ctx)}
        """);
        var text = WrapText("New booking", $"Hi {ctx.RecipientName},", "You have a new booking.", rows, MeetingTextLines(ctx));
        return new EmailContent(subject, html, text);
    }

    public static EmailContent CancellationConfirmation(BookingEmailContext ctx, string? reason, bool recipientIsOrganizer)
    {
        var subject = $"Cancelled: {ctx.ServiceTitle} with {(recipientIsOrganizer ? ctx.GuestName : ctx.OrganizerName)}";
        var rows = SummaryRows(ctx, includeLocationAndNotes: false)
            .Append(("Reason", string.IsNullOrWhiteSpace(reason) ? "—" : reason))
            .ToList();
        // Only the guest is offered another booking. The organizer's copy is a
        // notice about their own page, so pointing them at its public booking
        // screen would be the email telling them to book with themselves.
        var offerRebooking = !recipientIsOrganizer && !string.IsNullOrWhiteSpace(ctx.BookAgainUrl);
        var html = WrapLayout("Booking cancelled", $"""
            <p>Hi {Escape(ctx.RecipientName)},</p>
            <p>This booking has been cancelled.</p>
            {SummaryTable(rows)}
            {(offerRebooking ? BookAgainSection(ctx) : "")}
        """);
        var text = WrapText(
            "Booking cancelled", $"Hi {ctx.RecipientName},", "This booking has been cancelled.", rows,
            offerRebooking ? [$"Book another time: {ctx.BookAgainUrl}"] : []);
        return new EmailContent(subject, html, text);
    }

    public static EmailContent RescheduleConfirmation(
        BookingEmailContext ctx, string oldDateLabel, string oldTimeLabel, bool recipientIsOrganizer)
    {
        var subject = $"Rescheduled: {ctx.ServiceTitle} with {(recipientIsOrganizer ? ctx.GuestName : ctx.OrganizerName)}";
        var rows = new List<(string, string)>
        {
            ("Organizer", ctx.OrganizerName),
            ("Guest", ctx.GuestName),
            ("Service", ctx.ServiceTitle),
            ("Previous time", $"{oldDateLabel} at {oldTimeLabel}"),
            ("New time", $"{ctx.DateLabel} at {ctx.TimeLabel} ({ctx.TimeZone})"),
            ("Duration", FormatDuration(ctx.DurationMinutes)),
        };
        if (!string.IsNullOrWhiteSpace(ctx.MeetingLabel)) rows.Add(("Meeting", ctx.MeetingLabel));
        if (!string.IsNullOrWhiteSpace(ctx.Location)) rows.Add(("Location", ctx.Location));
        rows.Add(("Reference", ctx.BookingReference));

        var html = WrapLayout("Booking rescheduled", $"""
            <p>Hi {Escape(ctx.RecipientName)},</p>
            <p>This booking has been rescheduled.</p>
            {SummaryTable(rows)}
            {MeetingSection(ctx)}
            {(recipientIsOrganizer ? "" : Buttons(ctx, primaryLabel: "Reschedule again"))}
        """);
        // Both recipients get the join link: the meeting is the same one as
        // before (a reschedule moves the calendar event and keeps its
        // conference), but this email is the newest thing in either inbox, so
        // it is the one they will scroll back to when the time comes.
        var text = WrapText(
            "Booking rescheduled", $"Hi {ctx.RecipientName},", "This booking has been rescheduled.",
            rows, recipientIsOrganizer ? MeetingTextLines(ctx) : [.. MeetingTextLines(ctx), .. TextLinks(ctx)]);
        return new EmailContent(subject, html, text);
    }

    public static EmailContent Reminder(BookingEmailContext ctx, string windowLabel)
    {
        var subject = $"Reminder: {ctx.ServiceTitle} in {windowLabel}";
        var rows = SummaryRows(ctx);
        var html = WrapLayout("Upcoming booking reminder", $"""
            <p>Hi {Escape(ctx.RecipientName)},</p>
            <p>This is a reminder that your booking is coming up in {Escape(windowLabel)}.</p>
            {SummaryTable(rows)}
            {MeetingSection(ctx)}
            {Buttons(ctx)}
        """);
        // The reminder is the email that matters most here - it is the one open
        // in front of the recipient when the meeting starts. Note this is the
        // opposite call to the ICS attachment, which reminders deliberately do
        // NOT carry: a repeated attachment is four copies of the same file in a
        // calendar, whereas a repeated link is exactly the thing being reminded
        // about.
        var text = WrapText(
            "Upcoming booking reminder", $"Hi {ctx.RecipientName},",
            $"This is a reminder that your booking is coming up in {windowLabel}.", rows,
            [.. MeetingTextLines(ctx), .. TextLinks(ctx)]);
        return new EmailContent(subject, html, text);
    }

    /// <param name="includeAnswers">
    /// Only the two "here is the booking that was just made" emails carry the
    /// guest's custom-field answers: the confirmation and the organizer's new
    /// booking notice. A cancellation is about the booking ending, a reschedule
    /// about the time moving, and a reminder deliberately stays short - repeating
    /// the same answers in each of those is noise, the same call the reminder ICS
    /// attachment already makes.
    /// </param>
    private static List<(string Label, string Value)> SummaryRows(
        BookingEmailContext ctx,
        IEnumerable<(string Label, string Value)>? extraLeading = null,
        bool includeLocationAndNotes = true,
        bool includeAnswers = false)
    {
        var rows = new List<(string, string)> { ("Organizer", ctx.OrganizerName) };
        if (extraLeading is not null) rows.AddRange(extraLeading);
        rows.Add(("Service", ctx.ServiceTitle));
        rows.Add(("Date", ctx.DateLabel));
        rows.Add(("Time", ctx.TimeLabel));
        rows.Add(("Time zone", ctx.TimeZone));
        rows.Add(("Duration", FormatDuration(ctx.DurationMinutes)));
        if (includeLocationAndNotes)
        {
            // Stated as a row as well as drawn as a button, so an email read in
            // plain text - or with images and styling stripped - still says what
            // kind of meeting this is. Suppressed alongside Location for the
            // cancellation email, which is about the booking ending: there is
            // nothing left to join, so naming the meeting is just noise.
            // Location remains a separate concept either way - an online
            // meeting is not an address.
            if (!string.IsNullOrWhiteSpace(ctx.MeetingLabel)) rows.Add(("Meeting", ctx.MeetingLabel));
            if (!string.IsNullOrWhiteSpace(ctx.Location)) rows.Add(("Location", ctx.Location));
            if (!string.IsNullOrWhiteSpace(ctx.Notes)) rows.Add(("Notes", ctx.Notes));
        }
        // Before the reference, so the last row stays the support code it has
        // always been however many fields an organizer configures.
        if (includeAnswers) rows.AddRange(ctx.Answers.Select(a => (a.Label, a.Value)));
        rows.Add(("Reference", ctx.BookingReference));
        return rows;
    }

    private static string FormatDuration(int minutes) => minutes switch
    {
        <= 0 => "—",
        < 60 => $"{minutes} min",
        _ when minutes % 60 == 0 => $"{minutes / 60} hr",
        _ => $"{minutes / 60} hr {minutes % 60} min",
    };

    /// <summary>
    /// The "Join Google Meet" panel, or nothing at all when the booking has no
    /// meeting. Rendered from ctx.MeetingUrl - the single stored value the ICS
    /// attachment and every screen also read - so an email can never advertise
    /// a different link than the calendar entry sitting beside it.
    ///
    /// The raw URL is printed under the button on purpose: plenty of corporate
    /// mail clients strip or rewrite anchors, and a meeting the recipient can
    /// copy out of the text is still a meeting they can attend.
    /// </summary>
    private static string MeetingSection(BookingEmailContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.MeetingUrl)) return string.Empty;

        var label = string.IsNullOrWhiteSpace(ctx.MeetingLabel) ? "meeting" : ctx.MeetingLabel;
        return $"""
            <div style="margin:20px 0;padding:16px;border:1px solid {BorderColor};border-radius:8px;text-align:center;">
                <p style="margin:0 0 12px;color:{MutedColor};font-size:13px;">This is an online {Escape(label)}.</p>
                <a href="{ctx.MeetingUrl}" style="{JoinButtonStyle}">Join {Escape(label)}</a>
                <p style="margin:12px 0 0;color:{MutedColor};font-size:12px;word-break:break-all;">{Escape(ctx.MeetingUrl)}</p>
            </div>
        """;
    }

    /// <summary>The plain-text counterpart of <see cref="MeetingSection"/>, kept beside it so the two versions cannot drift.</summary>
    private static List<string> MeetingTextLines(BookingEmailContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.MeetingUrl)) return [];

        var label = string.IsNullOrWhiteSpace(ctx.MeetingLabel) ? "meeting" : ctx.MeetingLabel;
        return [$"Join {label}: {ctx.MeetingUrl}"];
    }

    /// <summary>
    /// The one action a cancellation email can usefully offer. Drawn as the
    /// primary button, because after a cancellation it is the only thing left
    /// to do - the manage/reschedule/cancel row those other emails carry would
    /// be three links to a booking that no longer exists.
    ///
    /// The URL is printed under the button for the same reason the join link is:
    /// plenty of corporate mail clients strip or rewrite anchors, and a booking
    /// page the recipient can copy out of the text is still one they can reach.
    /// </summary>
    private static string BookAgainSection(BookingEmailContext ctx) => $"""
        <div style="margin:20px 0 0;padding-top:16px;border-top:1px solid {BorderColor};text-align:center;">
            <p style="margin:0 0 12px;color:{MutedColor};font-size:13px;">Need a different time? You can book again whenever suits you.</p>
            <a href="{ctx.BookAgainUrl}" style="{ButtonStyle}">Book another time</a>
            <p style="margin:12px 0 0;color:{MutedColor};font-size:12px;word-break:break-all;">{Escape(ctx.BookAgainUrl!)}</p>
        </div>
    """;

    private static string Buttons(BookingEmailContext ctx, string primaryLabel = "Reschedule") => $"""
        <p style="margin-top:20px;">
            <a href="{ctx.ViewUrl}" style="{SecondaryButtonStyle}">View booking</a>
            &nbsp;
            <a href="{ctx.RescheduleUrl}" style="{ButtonStyle}">{Escape(primaryLabel)}</a>
            &nbsp;
            <a href="{ctx.CancelUrl}" style="{SecondaryButtonStyle}">Cancel</a>
        </p>
    """;

    private static List<string> TextLinks(BookingEmailContext ctx) =>
    [
        $"View booking: {ctx.ViewUrl}",
        $"Reschedule: {ctx.RescheduleUrl}",
        $"Cancel: {ctx.CancelUrl}",
    ];

    private static string SummaryTable(IEnumerable<(string Label, string Value)> rows)
    {
        var rowsHtml = string.Join("", rows.Select(r => $"""
            <tr>
                <td style="padding:6px 0;color:{MutedColor};font-size:13px;">{Escape(r.Label)}</td>
                <td style="padding:6px 0;color:{TextColor};font-size:13px;font-weight:500;text-align:right;">{Escape(r.Value)}</td>
            </tr>
        """));
        return $"""<table style="width:100%;border-collapse:collapse;margin:16px 0;">{rowsHtml}</table>""";
    }

    /// <summary>
    /// The shell every email is drawn in, matched to the guest-facing surface
    /// the same person just came from: <c>shell-100</c> behind a white card,
    /// <c>gray-200</c> border, <c>rounded-lg</c>. It was a cool <c>#f9fafb</c>
    /// page with a 12px radius, which is the generic centred-card look the
    /// palette note in index.css exists to avoid.
    ///
    /// The footer used to be <c>#9ca3af</c> - about 2.6:1 on white, below WCAG
    /// AA, and the same too-light grey the app removed from its own UI. Quiet
    /// text is <c>MutedColor</c> here for the same reason it is gray-500 there.
    /// </summary>
    private static string WrapLayout(string heading, string bodyHtml) => $"""
        <!doctype html>
        <html>
        <body style="margin:0;padding:24px;background:{PageBackgroundColor};font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;color:{TextColor};">
            <div style="max-width:480px;margin:0 auto;background:#ffffff;border:1px solid {BorderColor};border-radius:8px;padding:32px;">
                <h1 style="font-size:16px;font-weight:600;margin:0 0 16px;">{Escape(heading)}</h1>
                {bodyHtml}
                <p style="margin-top:24px;padding-top:16px;border-top:1px solid {BorderColor};color:{MutedColor};font-size:12px;">
                    Sent by BookingTracker.
                </p>
            </div>
        </body>
        </html>
        """;

    private static string WrapText(
        string heading, string greeting, string intro, IEnumerable<(string Label, string Value)> rows, IEnumerable<string> links)
    {
        var lines = new List<string> { heading, new string('=', heading.Length), "", greeting, intro, "" };
        lines.AddRange(rows.Select(r => $"{r.Label}: {r.Value}"));
        if (links.Any())
        {
            lines.Add("");
            lines.AddRange(links);
        }
        lines.Add("");
        lines.Add("Sent by BookingTracker.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
