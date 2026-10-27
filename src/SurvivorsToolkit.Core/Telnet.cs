using System.Net.Sockets;
using System.Text;

namespace SurvivorsToolkit.Core;

/// <summary>Where a server's telnet console is (saved in the settings; the password is encrypted by <see cref="Secrets"/>).</summary>
public sealed class TelnetTarget
{
    /// <summary>The server's address: 127.0.0.1 for one on this PC, else the host your game host gave you.</summary>
    public string Host { get; set; } = "127.0.0.1";
    /// <summary>The TelnetPort from serverconfig.xml; 8081 is the game's default.</summary>
    public int Port { get; set; } = 8081;
    /// <summary>The TelnetPassword, encrypted for this Windows user; never plain text.</summary>
    public string? ProtectedPassword { get; set; }
    /// <summary>How often a blood moon comes (BloodMoonFrequency in serverconfig.xml), for the countdown.</summary>
    public int BloodMoonFrequency { get; set; } = 7;

    public bool IsSet => Host.Trim().Length > 0 && Port > 0;

    public string Describe => $"{Host.Trim()}:{Port}";
}

/// <summary>The telnet console said no, or said nothing we understood.</summary>
public sealed class ServerConsoleException : Exception
{
    public ServerConsoleException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>A server's console: send a command, get back what it printed.</summary>
public interface IServerConsole : IDisposable
{
    /// <summary>What the console printed before and while logging in (the game's banner).</summary>
    string Banner { get; }

    /// <summary>Sends one command and returns the lines it printed. Throws <see cref="ServerConsoleException"/> when the link is gone.</summary>
    Task<string> SendAsync(string command, CancellationToken ct);
}

/// <summary>
/// 7 Days to Die's telnet console (TelnetEnabled in serverconfig.xml): a plain TCP link that asks for the password,
/// then takes one command per line and prints the answer. There is no end-of-answer marker, so a command's answer is
/// what arrives before the console goes quiet for <see cref="Quiet"/>.
/// </summary>
public sealed class TelnetConsole : IServerConsole
{
    /// <summary>How long the console must say nothing for an answer to count as finished.</summary>
    public static TimeSpan Quiet { get; set; } = TimeSpan.FromMilliseconds(400);
    /// <summary>The longest wait for the first byte of an answer.</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly byte[] _buffer = new byte[8192];

    TelnetConsole(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public string Banner { get; private set; } = "";

    /// <summary>
    /// Opens the console and logs in. Throws <see cref="ServerConsoleException"/> when the server can't be reached or
    /// the password is wrong.
    /// </summary>
    public static async Task<TelnetConsole> ConnectAsync(string host, int port, string password, CancellationToken ct = default)
    {
        var client = new TcpClient();
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connect.CancelAfter(Timeout);
            await client.ConnectAsync(host.Trim(), port, connect.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new ServerConsoleException($"Couldn't reach the console at {host.Trim()}:{port}. Check the address, the port, and that TelnetEnabled is true in the server's config.", e);
        }
        var console = new TelnetConsole(client);
        try
        {
            var hello = await console.ReadAsync(ct).ConfigureAwait(false);
            if (hello.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                if (password.Length == 0) throw new ServerConsoleException("The console asked for a password. Fill in the TelnetPassword from the server's config.");
                hello += await console.WriteReadAsync(password, ct).ConfigureAwait(false);
                if (hello.Contains("Password incorrect", StringComparison.OrdinalIgnoreCase)
                    || hello.Contains("password", StringComparison.OrdinalIgnoreCase) && !hello.Contains("Logon successful", StringComparison.OrdinalIgnoreCase))
                    throw new ServerConsoleException("The console didn't take that password.");
            }
            console.Banner = hello.Trim();
            return console;
        }
        catch
        {
            console.Dispose();
            throw;
        }
    }

    public async Task<string> SendAsync(string command, CancellationToken ct) =>
        Clean(await WriteReadAsync(command, ct).ConfigureAwait(false));

    async Task<string> WriteReadAsync(string line, CancellationToken ct)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            throw new ServerConsoleException("The console link is gone. Connect again.", e);
        }
        return await ReadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads until the console has said nothing for <see cref="Quiet"/>, or nothing at all within <see cref="Timeout"/>.</summary>
    async Task<string> ReadAsync(CancellationToken ct)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int read;
            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(bytes.Count == 0 ? Timeout : Quiet);
                read = await _stream.ReadAsync(_buffer, wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Quiet: the answer is finished. Nothing at all within Timeout means the server ignored the command.
                break;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
            {
                if (bytes.Count > 0) break;
                throw new ServerConsoleException("The console link is gone. Connect again.", e);
            }
            if (read == 0) break;
            bytes.AddRange(_buffer.Take(read));
        }
        return Encoding.UTF8.GetString(Strip(bytes).ToArray());
    }

    /// <summary>Drops telnet's own negotiation bytes (IAC sequences), which the game never sends but a proxy might.</summary>
    internal static List<byte> Strip(List<byte> bytes)
    {
        const byte iac = 255;
        if (!bytes.Contains(iac)) return bytes;
        var o = new List<byte>(bytes.Count);
        for (var i = 0; i < bytes.Count; i++)
        {
            if (bytes[i] != iac) { o.Add(bytes[i]); continue; }
            // IAC: a two-byte command, or three when it is WILL/WONT/DO/DONT (251-254).
            i += i + 1 < bytes.Count && bytes[i + 1] >= 251 && bytes[i + 1] <= 254 ? 2 : 1;
        }
        return o;
    }

    /// <summary>Tidies an answer for showing: normal line ends, no trailing blank lines.</summary>
    internal static string Clean(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ', '\t');

    public void Dispose()
    {
        try { _stream.Dispose(); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        _client.Dispose();
    }
}
