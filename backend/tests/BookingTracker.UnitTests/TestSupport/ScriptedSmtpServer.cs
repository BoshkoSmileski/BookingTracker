using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// A throwaway SMTP server on a loopback port that answers one nominated
/// command with a nominated reply, so a test can make System.Net.Mail.SmtpClient
/// produce a real failure of a chosen kind.
///
/// This exists because SmtpFailureClassifier's whole job is reading reply codes
/// off a real SmtpException, and a hand-constructed SmtpException would only
/// prove the classifier agrees with the test's own guess about what the library
/// does. It does not always do the obvious thing: SmtpClient IGNORES a 535
/// rejection of the AUTH command outright and carries on unauthenticated, so
/// what a bad password actually surfaces as is a 530 at MAIL FROM. A test built
/// on the assumption instead of the observation would have pinned the wrong code.
///
/// Speaks only enough SMTP for one message: no TLS, no pipelining, no AUTH
/// unless a test asks for it to be advertised.
/// </summary>
public sealed class ScriptedSmtpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    private ScriptedSmtpServer(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    /// <summary>
    /// Answers <paramref name="failingCommand"/> (an SMTP verb such as "MAIL",
    /// or "." for end-of-data) with <paramref name="failureReply"/>, and every
    /// other command with a success reply.
    /// </summary>
    public static ScriptedSmtpServer RejectingAt(string failingCommand, string failureReply)
        => Start(line => Matches(line, failingCommand) ? failureReply : SuccessReply(line));

    /// <summary>Accepts everything - the control case for a clean send.</summary>
    public static ScriptedSmtpServer Accepting() => Start(SuccessReply);

    private static bool Matches(string line, string command) =>
        command == "." ? line == "." : line.StartsWith(command, StringComparison.OrdinalIgnoreCase);

    private static string SuccessReply(string line)
    {
        if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) return "250 test";
        if (line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase)) return "250 test";
        if (line.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase)) return "250 OK";
        if (line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase)) return "250 OK";
        if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase)) return "354 Go ahead";
        if (line == ".") return "250 Queued";
        if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) return "221 Bye";
        return "250 OK";
    }

    private static ScriptedSmtpServer Start(Func<string, string> respond)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new ScriptedSmtpServer(listener, ((IPEndPoint)listener.LocalEndpoint).Port);
        _ = server.ServeAsync(respond);
        return server;
    }

    private async Task ServeAsync(Func<string, string> respond)
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

            await writer.WriteLineAsync("220 test ESMTP");

            var inData = false;
            string? line;
            while ((line = await reader.ReadLineAsync(_cts.Token)) is not null)
            {
                // Inside DATA the server must stay silent until the lone ".";
                // answering each body line desyncs the conversation and makes
                // the client look like it is misbehaving when it is not.
                if (inData)
                {
                    if (line != ".") continue;
                    inData = false;
                }

                var reply = respond(line);
                await writer.WriteLineAsync(reply);
                if (reply.StartsWith("354")) inData = true;
                if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        catch
        {
            // The client hanging up mid-conversation is the normal end of a
            // rejection case, not a failure of the test.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
