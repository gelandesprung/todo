using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DayTodo.Mcp;

/// <summary>
/// MCP Streamable HTTP host embedded in the WPF process (POST /mcp, optional SSE replies).
/// Raw TcpListener: no NuGet packages, no http.sys URL reservation, loopback only.
/// Start/Stop are driven by the in-app switch, so no second process is needed.
/// </summary>
public sealed class McpHttpHost
{
    private readonly McpServer _server;
    private readonly string? _token;
    private readonly int _port;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private TcpListener? _listener;
    private Thread? _thread;

    public McpHttpHost(McpServer server, int port, string? token)
    {
        _server = server;
        _port = port;
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public bool IsRunning => _listener != null;

    /// <summary>Throws a SocketException when the port is already taken.</summary>
    public void Start()
    {
        if (_listener != null) return;
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        _listener = listener;
        _thread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "DayTodo.Mcp"
        };
        _thread.Start();
        System.Diagnostics.Debug.WriteLine(
            $"DayTodo MCP: http://127.0.0.1:{_port}/mcp (session {_sessionId})");
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        try { listener?.Stop(); }
        catch (Exception) { }
    }

    private void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch (Exception) { return; }

            _ = Task.Run(() =>
            {
                try { Serve(client); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("DayTodo MCP HTTP: " + ex.Message);
                }
                finally { client.Dispose(); }
            });
        }
    }

    private void Serve(TcpClient client)
    {
        client.NoDelay = true;
        using var stream = client.GetStream();
        using var buffered = new BufferedStream(stream);

        var request = ReadRequest(buffered);
        if (!request.ok) return;

        var headers = request.headers;
        if (!IsLocalOrigin(headers))
        {
            Respond(buffered, 403, "Forbidden", "application/json; charset=utf-8",
                Utf8("{\"error\":\"origin not allowed\"}"));
            return;
        }

        if (_token != null)
        {
            var auth = headers.TryGetValue("Authorization", out var a) ? a : string.Empty;
            if (!string.Equals(auth, "Bearer " + _token, StringComparison.Ordinal))
            {
                Respond(buffered, 401, "Unauthorized", "application/json; charset=utf-8",
                    Utf8("{\"error\":\"invalid bearer token\"}"),
                    ("WWW-Authenticate", "Bearer"));
                return;
            }
        }

        var sessionHeader = ("Mcp-Session-Id", _sessionId);

        if (string.Equals(request.method, "OPTIONS", StringComparison.Ordinal))
        {
            Respond(buffered, 204, "No Content", null, null,
                ("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS"),
                ("Access-Control-Allow-Headers",
                    "Content-Type, Authorization, Accept, Mcp-Session-Id, MCP-Protocol-Version"),
                ("Access-Control-Allow-Origin", "*"));
            return;
        }

        var path = request.path.Split('?')[0].TrimEnd('/');
        if (path.Length == 0) path = "/";
        if (!string.Equals(path, "/mcp", StringComparison.Ordinal))
        {
            Respond(buffered, 404, "Not Found", "application/json; charset=utf-8",
                Utf8("{\"error\":\"use POST /mcp\"}"));
            return;
        }

        if (string.Equals(request.method, "GET", StringComparison.Ordinal))
        {
            // Server-initiated SSE is optional in Streamable HTTP; 405 is spec compliant.
            Respond(buffered, 405, "Method Not Allowed", "application/json; charset=utf-8",
                Utf8("{\"error\":\"GET SSE not supported; use POST /mcp\"}"),
                ("Allow", "POST, DELETE, OPTIONS"));
            return;
        }

        if (string.Equals(request.method, "DELETE", StringComparison.Ordinal))
        {
            Respond(buffered, 204, "No Content", null, null);
            return;
        }

        if (!string.Equals(request.method, "POST", StringComparison.Ordinal))
        {
            Respond(buffered, 405, "Method Not Allowed", null, null,
                ("Allow", "POST, DELETE, OPTIONS"));
            return;
        }

        string json;
        try { json = Encoding.UTF8.GetString(request.body); }
        catch (Exception)
        {
            Respond(buffered, 400, "Bad Request", null, null);
            return;
        }

        var response = _server.Handle(json);
        if (response == null)
        {
            // Notifications / empty batches: 202 Accepted, no body.
            Respond(buffered, 202, "Accepted", null, null, sessionHeader);
            return;
        }

        var accept = headers.TryGetValue("Accept", out var a2) ? a2 : string.Empty;
        var wantsSseOnly = accept.Contains("text/event-stream", StringComparison.Ordinal)
                           && !accept.Contains("application/json", StringComparison.Ordinal);

        if (wantsSseOnly)
        {
            var sse = "event: message\ndata: " + response.Replace("\n", " ") + "\n\n";
            Respond(buffered, 200, "OK", "text/event-stream; charset=utf-8",
                Utf8(sse), sessionHeader);
        }
        else
        {
            Respond(buffered, 200, "OK", "application/json; charset=utf-8",
                Utf8(response), sessionHeader);
        }
    }

    /// <summary>
    /// Byte-level HTTP/1.1 request reader. Content-Length counts bytes, so the body is
    /// read from the same BufferedStream as the headers (no StreamReader prefetch loss).
    /// </summary>
    private static (bool ok, string method, string path, Dictionary<string, string> headers, byte[] body)
        ReadRequest(Stream s)
    {
        var empty = (false, string.Empty, string.Empty,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<byte>());

        var acc = new List<byte>();
        while (true)
        {
            var b = s.ReadByte();
            if (b < 0) return empty;
            acc.Add((byte)b);
            var n = acc.Count;
            if (n >= 4 && acc[n - 4] == 13 && acc[n - 3] == 10 &&
                acc[n - 2] == 13 && acc[n - 1] == 10) break;
            if (n > 64 * 1024) return empty;
        }

        var lines = Encoding.ASCII.GetString(acc.ToArray())
            .Split(new[] { "\r\n" }, StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length < 2) return empty;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
        }

        var length = 0;
        if (headers.TryGetValue("Content-Length", out var cl)) int.TryParse(cl, out length);
        if (length < 0 || length > 4 * 1024 * 1024) return empty;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = s.Read(body, read, length - read);
            if (n <= 0) break;
            read += n;
        }
        if (read < length) Array.Resize(ref body, read);

        return (true, first[0], first[1], headers, body);
    }

    private static void Respond(Stream s, int status, string reason, string? contentType,
        byte[]? body, params (string key, string value)[] extra)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        if (contentType != null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        foreach (var (key, value) in extra)
            sb.Append(key).Append(": ").Append(value).Append("\r\n");
        sb.Append("\r\n");

        var head = Encoding.ASCII.GetBytes(sb.ToString());
        s.Write(head, 0, head.Length);
        if (body != null && body.Length > 0) s.Write(body, 0, body.Length);
        s.Flush();
    }

    private static bool IsLocalOrigin(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Origin", out var origin) || string.IsNullOrEmpty(origin)) return true;
        // Desktop clients (Electron / file:// pages) normally send "null" or "file://".
        // They are local too; only remote http(s) origins should be rejected.
        if (origin.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return uri.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
