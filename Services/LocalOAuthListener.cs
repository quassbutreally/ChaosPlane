using System.Net;

namespace ChaosPlane.Services;

/// <summary>
/// Spins up a temporary HTTP listener on localhost to capture the Twitch
/// implicit-grant OAuth token from the browser redirect.
///
/// Flow:
///   1. Browser is sent to Twitch auth URL with redirect_uri = http://localhost:7842/chaosplane/callback
///   2. Twitch redirects to that URI with #access_token=XXX in the fragment
///   3. We serve a tiny HTML page that reads the fragment with JS and calls /token?access_token=XXX
///   4. We capture the token from that second request and return it to the caller
/// </summary>
public class LocalOAuthListener : IDisposable
{
    private const string Host       = "http://localhost:7842/";
    private const string CallbackPath = "/chaosplane/callback";
    private const string TokenPath    = "/chaosplane/token";

    public const string RedirectUri = "http://localhost:7842/chaosplane/callback";

    private readonly HttpListener         _listener;
    private readonly CancellationTokenSource _cts = new();

    public LocalOAuthListener()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(Host);
    }

    /// <summary>
    /// Starts listening and returns the captured access token, or null on timeout/cancel.
    /// </summary>
    public async Task<string?> WaitForTokenAsync(TimeSpan timeout)
    {
        _listener.Start();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(
            _cts.Token, timeoutCts.Token);

        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                var contextTask = _listener.GetContextAsync();
                var completed   = await Task.WhenAny(
                    contextTask,
                    Task.Delay(Timeout.Infinite, linked.Token));

                if (completed != contextTask) break;

                var ctx  = await contextTask;
                var path = ctx.Request.Url?.AbsolutePath ?? string.Empty;

                if (path.Equals(CallbackPath, StringComparison.OrdinalIgnoreCase))
                {
                    await ServeTokenExtractorPageAsync(ctx.Response);
                    continue;
                }

                if (path.Equals(TokenPath, StringComparison.OrdinalIgnoreCase))
                {
                    var token = ctx.Request.QueryString["access_token"];
                    await ServeSuccessPageAsync(ctx.Response);
                    return token;
                }

                // Unknown path — 404
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }

        return null;
    }

    /// <summary>
    /// Serves a minimal HTML page that reads #access_token from the fragment
    /// and POSTs it back to /chaosplane/token as a query param.
    /// </summary>
    private static async Task ServeTokenExtractorPageAsync(HttpListenerResponse response)
    {
        const string html = """
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8">
                <title>ChaosPlane — Authorising...</title>
                <style>
                    body { background: #0d0f0e; color: #d4ddd8; font-family: Consolas, monospace;
                           display: flex; align-items: center; justify-content: center;
                           height: 100vh; margin: 0; }
                    .box { text-align: center; }
                    .amber { color: #ffa500; font-size: 1.4em; font-weight: bold;
                             letter-spacing: .15em; }
                    .dim { color: #6e847a; margin-top: .5em; font-size: .85em; }
                </style>
            </head>
            <body>
                <div class="box">
                    <div class="amber">CHAOSPLANE</div>
                    <div class="dim" id="status">Capturing authorisation token...</div>
                </div>
                <script>
                    const fragment = window.location.hash.substring(1);
                    const params   = new URLSearchParams(fragment);
                    const token    = params.get('access_token');

                    if (token) {
                        fetch('/chaosplane/token?access_token=' + encodeURIComponent(token))
                            .then(() => {
                                document.getElementById('status').textContent =
                                    'Authorised! You can close this tab.';
                            });
                    } else {
                        document.getElementById('status').textContent =
                            'Error: no token found. Please try again.';
                    }
                </script>
            </body>
            </html>
            """;

        await WriteHtmlResponseAsync(response, html);
    }

    private static async Task ServeSuccessPageAsync(HttpListenerResponse response)
    {
        const string html = """
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8">
                <title>ChaosPlane — Authorised</title>
                <style>
                    body { background: #0d0f0e; color: #d4ddd8; font-family: Consolas, monospace;
                           display: flex; align-items: center; justify-content: center;
                           height: 100vh; margin: 0; }
                    .box { text-align: center; }
                    .green { color: #00e676; font-size: 1.4em; font-weight: bold;
                             letter-spacing: .15em; }
                    .dim { color: #6e847a; margin-top: .5em; font-size: .85em; }
                </style>
            </head>
            <body>
                <div class="box">
                    <div class="green">✓ AUTHORISED</div>
                    <div class="dim">ChaosPlane is connected. You can close this tab.</div>
                </div>
            </body>
            </html>
            """;

        await WriteHtmlResponseAsync(response, html);
    }

    private static async Task WriteHtmlResponseAsync(HttpListenerResponse response, string html)
    {
        response.ContentType     = "text/html; charset=utf-8";
        response.StatusCode      = 200;
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        _cts.Dispose();
    }
}
