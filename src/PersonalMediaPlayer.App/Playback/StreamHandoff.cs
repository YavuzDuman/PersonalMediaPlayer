using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32;

namespace PersonalMediaPlayer.App.Playback;

internal static class StreamHandoff
{
    internal const string Scheme = "personalmediaplayer";

    internal const string MutexName = @"Local\PersonalMediaPlayer.SingleInstance";

    internal const string PipeName = "PersonalMediaPlayer.StreamHandoff";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static string LaunchCommand(string exePath) => $"\"{exePath}\" \"%1\"";

    internal static string? ProtocolArgument(IReadOnlyList<string> args)
    {
        for (var i = 1; i < args.Count; i++)
        {
            var candidate = args[i].Trim().Trim('"');
            if (candidate.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    internal const double MaxStartSeconds = 7 * 24 * 60 * 60;

    internal readonly record struct StreamLaunch(Uri? Url, Uri? Referrer, long? StartMs, bool Resolve = false);

    internal static bool TryParseProtocol(string? argument, out string? address)
        => TryParseProtocol(argument, out address, out _, out _, out _);

    internal static bool TryParseProtocol(string? argument, out string? address, out string? referrer, out double? startSeconds)
        => TryParseProtocol(argument, out address, out referrer, out startSeconds, out _);

    internal static bool TryParseProtocol(string? argument, out string? address, out string? referrer, out double? startSeconds, out bool resolve)
    {
        address = null;
        referrer = null;
        startSeconds = null;
        resolve = false;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var text = argument.Trim().Trim('"');
        if (!text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = uri.Query;
        if (query.Length == 0)
        {
            var mark = text.IndexOf('?');
            if (mark >= 0)
            {
                query = text[mark..];
            }
        }

        address = QueryValue(query, "url");
        referrer = QueryValue(query, "referrer");
        startSeconds = ParseStartSeconds(QueryValue(query, "t"));
        resolve = string.Equals(QueryValue(query, "resolve"), "1", StringComparison.Ordinal);
        return true;
    }

    internal static bool TryReadLaunch(string? argument, out StreamLaunch launch)
    {
        launch = default;
        if (!TryParseProtocol(argument, out var address, out var referrerText, out var startSeconds, out var resolve))
        {
            return false;
        }

        Uri? url = null;
        if (!string.IsNullOrWhiteSpace(address) && StreamLink.TryNormalize(address, out var normalized))
        {
            url = normalized;
        }

        launch = new StreamLaunch(url, HttpReferrer(referrerText), StartMs(startSeconds), resolve);
        return true;
    }

    private static Uri? HttpReferrer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return StreamLink.TryNormalize(trimmed, out var referrer) ? referrer : null;
    }

    private static long? StartMs(double? seconds)
    {
        if (seconds is not double value || value <= 0 || value > MaxStartSeconds)
        {
            return null;
        }

        var ms = (long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero);
        return ms > 0 ? ms : null;
    }

    private static double? ParseStartSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds)
            || seconds < 0)
        {
            return null;
        }

        return seconds;
    }

    internal static bool TryOwn(string name, out Mutex? mutex)
    {
        mutex = null;
        Mutex? created = null;
        try
        {
            created = new Mutex(false, name);
            try
            {
                if (!created.WaitOne(0))
                {
                    created.Dispose();
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                mutex = created;
                return true;
            }

            mutex = created;
            return true;
        }
        catch (Exception)
        {
            created?.Dispose();
            mutex = null;
            return true;
        }
    }

    internal static void RegisterProtocol(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme);
        key.SetValue(string.Empty, "URL:Personal Media Player");
        key.SetValue("URL Protocol", string.Empty);
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, LaunchCommand(exePath));
    }

    internal static async Task ListenAsync(string pipeName, Func<string, Task> onLine, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                using var wake = cancellationToken.Register(() => WakeListener(pipeName));
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                string? line;
                using (var reader = new StreamReader(pipe, Utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }

                if (line is not null)
                {
                    try
                    {
                        await onLine(line).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // One failed handoff keeps the listener running for the next address.
                    }
                }

                try
                {
                    await pipe.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                try
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    internal static async Task SendAsync(string pipeName, string line, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        await using var writer = new StreamWriter(pipe, Utf8, bufferSize: 1024, leaveOpen: true);
        var singleLine = line.Replace("\r", string.Empty).Replace("\n", string.Empty);
        await writer.WriteLineAsync(singleLine.AsMemory(), timeout.Token).ConfigureAwait(false);
        await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private static void WakeListener(string pipeName)
    {
        try
        {
            using var wake = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            wake.Connect(200);
        }
        catch (Exception)
        {
            // The listener is already gone, or another client took the connection.
        }
    }

    private static string? QueryValue(string query, string key)
    {
        if (query.Length == 0)
        {
            return null;
        }

        var text = query[0] == '?' ? query[1..] : query;
        foreach (var part in text.Split('&'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var separator = part.IndexOf('=');
            var name = separator >= 0 ? part[..separator] : part;
            if (!string.Equals(Unescape(name), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return separator >= 0 ? Unescape(part[(separator + 1)..]) : string.Empty;
        }

        return null;
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
