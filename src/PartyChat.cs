using System;

using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ReadyCheck;

/// <summary>
/// Sends one line to party chat.
///
/// <para>Dalamud has no API for this. <c>IChatGui.Print</c> only writes to your own
/// log — nobody else sees it — and there is no <c>Send</c>. The only route to the
/// wire is <c>UIModule.ProcessChatBoxEntry</c>, the function the chat box itself
/// calls on Enter, which is why the text has to be prefixed with <c>/p</c> rather
/// than given a channel argument.</para>
///
/// <para>That makes the prefix the entire guarantee of which channel this lands in,
/// so <see cref="Sanitise"/> is load-bearing rather than tidiness: a newline in the
/// message would end the command and run whatever followed as a second one.</para>
/// </summary>
internal static unsafe class PartyChat
{
    /// <summary>The game truncates beyond this; trim before it does, and at a word.</summary>
    private const int MaxMessageBytes = 400;

    /// <summary>
    /// The exact chat-box line <see cref="Send"/> would submit, or null if nothing
    /// survives sanitising. Split out from Send so a dry run can show the real line
    /// rather than a reconstruction of it that could drift.
    /// </summary>
    public static string? BuildLine(string message)
    {
        var text = Sanitise(message);
        return text.Length == 0 ? null : "/p " + text;
    }

    /// <summary>
    /// Sends <paramref name="message"/> to party chat. Returns the line as sent, or
    /// null if there was nothing left to send after sanitising.
    /// </summary>
    public static string? Send(string message)
    {
        var line = BuildLine(message);
        if (line == null)
            return null;

        var ui = UIModule.Instance();
        if (ui == null)
            return null;

        // ProcessChatBoxEntry reads a Utf8String the game allocated. Borrowing a
        // managed buffer is not an option, so allocate in the game's heap and free
        // it again even if the call throws.
        var utf8 = Utf8String.CreateEmpty(IMemorySpace.GetDefaultSpace());
        if (utf8 == null)
            return null;

        try
        {
            utf8->SetString(line);
            ui->ProcessChatBoxEntry(utf8, IntPtr.Zero, false);
        }
        finally
        {
            utf8->Dtor(true);
        }

        return line;
    }

    /// <summary>
    /// Flattens the message to a single safe chat line: no line breaks (each would
    /// start a fresh command), no control characters, no leading slash (which would
    /// make the message itself a command), and short enough for the game to accept.
    /// </summary>
    private static string Sanitise(string message)
    {
        Span<char> buffer = stackalloc char[Math.Min(message.Length, 1024)];
        var        length = 0;

        foreach (var c in message)
        {
            if (length == buffer.Length)
                break;
            // Newlines and tabs become spaces so words do not run together; other
            // control characters are dropped outright.
            if (c is '\n' or '\r' or '\t')
                buffer[length++] = ' ';
            else if (!char.IsControl(c))
                buffer[length++] = c;
        }

        var text = new string(buffer[..length]).Trim();

        // A leading slash would be parsed as a command rather than sent to the party.
        while (text.StartsWith('/'))
            text = text[1..].TrimStart();

        return Truncate(text);
    }

    private static string Truncate(string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) <= MaxMessageBytes)
            return text;

        // Cut on a character boundary by shrinking until it fits, then back up to the
        // last space so the line does not end mid-word.
        var end = Math.Min(text.Length, MaxMessageBytes);
        while (end > 0 && System.Text.Encoding.UTF8.GetByteCount(text[..end]) > MaxMessageBytes - 1)
            end--;

        var cut      = text[..end];
        var lastWord = cut.LastIndexOf(' ');
        if (lastWord > cut.Length / 2)
            cut = cut[..lastWord];

        return cut.TrimEnd() + "…";
    }
}
