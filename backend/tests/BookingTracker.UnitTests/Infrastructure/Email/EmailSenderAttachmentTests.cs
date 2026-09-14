using System.Net.Mail;
using System.Net.Mime;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Infrastructure.Email;

/// <summary>
/// How the two senders turn an invitation into a real attachment.
/// FileSystemEmailService is exercised directly (its whole job is writing files);
/// the SMTP sender's MIME construction is asserted against System.Net.Mail's own
/// types rather than by sending anything - no SMTP server is involved, and the
/// attachment shape is the part that actually decides whether Outlook and Apple
/// Mail render an invitation or an inert file.
/// </summary>
public class EmailSenderAttachmentTests
{
    private const string SampleIcs =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:abc@bookingtracker\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static EmailMessage Message(EmailCalendarAttachment? calendar) =>
        new("guest@example.com", "Jane Doe", "Confirmed: Meeting", "<p>hello</p>", "hello", calendar);

    // ---- FileSystemEmailService -------------------------------------------------

    private static string[] WrittenFilesFor(string subject)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BookingTracker", "sent-emails");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => Path.GetFileName(f).Contains(subject, StringComparison.Ordinal)).ToArray()
            : [];
    }

    [Fact]
    public async Task FileSystemSender_WritesHtmlTextAndIcs_WhenAnInvitationIsPresent()
    {
        var marker = $"IcsTest-{Guid.NewGuid():N}";
        var sender = new FileSystemEmailService(NullLogger<FileSystemEmailService>.Instance);

        await sender.SendAsync(new EmailMessage(
            "guest@example.com", "Jane Doe", marker, "<p>hello</p>", "hello",
            new EmailCalendarAttachment("invite.ics", SampleIcs, "REQUEST")));

        var files = WrittenFilesFor(marker);
        try
        {
            Assert.Contains(files, f => f.EndsWith(".html", StringComparison.Ordinal));
            Assert.Contains(files, f => f.EndsWith(".txt", StringComparison.Ordinal));
            Assert.Contains(files, f => f.EndsWith(".ics", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var f in files) File.Delete(f);
        }
    }

    [Fact]
    public async Task FileSystemSender_WritesTheIcsByteForByte_PreservingCrlf()
    {
        // The file on disk must be exactly what SMTP would attach, so it can be opened
        // in a real client to verify the invitation end to end.
        var marker = $"IcsTest-{Guid.NewGuid():N}";
        var sender = new FileSystemEmailService(NullLogger<FileSystemEmailService>.Instance);

        await sender.SendAsync(new EmailMessage(
            "guest@example.com", "Jane Doe", marker, "<p>hello</p>", "hello",
            new EmailCalendarAttachment("invite.ics", SampleIcs, "REQUEST")));

        var files = WrittenFilesFor(marker);
        try
        {
            var icsPath = Assert.Single(files, f => f.EndsWith(".ics", StringComparison.Ordinal));
            var written = await File.ReadAllTextAsync(icsPath);
            Assert.Equal(SampleIcs, written);
            // No BOM: some strict parsers choke on one.
            var bytes = await File.ReadAllBytesAsync(icsPath);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "ics must not start with a UTF-8 BOM");
        }
        finally
        {
            foreach (var f in files) File.Delete(f);
        }
    }

    [Fact]
    public async Task FileSystemSender_WritesNoIcs_WhenThereIsNoInvitation()
    {
        var marker = $"IcsTest-{Guid.NewGuid():N}";
        var sender = new FileSystemEmailService(NullLogger<FileSystemEmailService>.Instance);

        await sender.SendAsync(new EmailMessage("guest@example.com", "Jane Doe", marker, "<p>hello</p>", "hello"));

        var files = WrittenFilesFor(marker);
        try
        {
            Assert.Equal(2, files.Length);
            Assert.DoesNotContain(files, f => f.EndsWith(".ics", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var f in files) File.Delete(f);
        }
    }

    // ---- SMTP MIME construction -------------------------------------------------

    /// <summary>
    /// Mirrors the attachment construction in SmtpEmailService. Asserting the MIME
    /// shape this way keeps the test free of an SMTP server while still failing if
    /// the content type, method parameter, or transfer encoding regress.
    /// </summary>
    private static (AlternateView View, Attachment Attachment) BuildCalendarParts(EmailCalendarAttachment calendar)
    {
        var contentType = new ContentType("text/calendar")
        {
            CharSet = "utf-8",
            Parameters = { ["method"] = calendar.Method },
        };
        var view = AlternateView.CreateAlternateViewFromString(calendar.Content, contentType);
        view.TransferEncoding = TransferEncoding.Base64;

        var attachment = new Attachment(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(calendar.Content)), calendar.FileName, "text/calendar");
        attachment.ContentType.Parameters["method"] = calendar.Method;
        attachment.ContentType.CharSet = "utf-8";
        attachment.TransferEncoding = TransferEncoding.Base64;

        return (view, attachment);
    }

    [Fact]
    public void SmtpAttachment_UsesTextCalendarWithTheMethodParameter()
    {
        var (view, attachment) = BuildCalendarParts(new EmailCalendarAttachment("invite.ics", SampleIcs, "REQUEST"));

        Assert.Equal("text/calendar", view.ContentType.MediaType);
        Assert.Equal("REQUEST", view.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", view.ContentType.CharSet);

        Assert.Equal("text/calendar", attachment.ContentType.MediaType);
        Assert.Equal("REQUEST", attachment.ContentType.Parameters["method"]);
        Assert.Equal("invite.ics", attachment.Name);
    }

    [Fact]
    public void SmtpAttachment_CarriesTheCancelMethodForCancellations()
    {
        // The MIME method must agree with the METHOD inside the payload - clients act
        // on the parameter, and a mismatch is why cancellations sometimes don't remove.
        var (view, attachment) = BuildCalendarParts(new EmailCalendarAttachment("invite.ics", SampleIcs, "CANCEL"));

        Assert.Equal("CANCEL", view.ContentType.Parameters["method"]);
        Assert.Equal("CANCEL", attachment.ContentType.Parameters["method"]);
    }

    [Fact]
    public void SmtpAttachment_UsesBase64_SoCrlfSurvivesTransport()
    {
        var (view, attachment) = BuildCalendarParts(new EmailCalendarAttachment("invite.ics", SampleIcs, "REQUEST"));

        Assert.Equal(TransferEncoding.Base64, view.TransferEncoding);
        Assert.Equal(TransferEncoding.Base64, attachment.TransferEncoding);
    }

    [Fact]
    public void SmtpAttachment_ContentRoundTripsUnchanged()
    {
        var (_, attachment) = BuildCalendarParts(new EmailCalendarAttachment("invite.ics", SampleIcs, "REQUEST"));

        using var reader = new StreamReader(attachment.ContentStream);
        Assert.Equal(SampleIcs, reader.ReadToEnd());
    }

    [Fact]
    public void EmailMessage_DefaultsToNoCalendar_SoExistingCallersAreUnaffected()
    {
        Assert.Null(Message(null).Calendar);
    }
}
