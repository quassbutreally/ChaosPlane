using ChaosPlane.EBS.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.Configure<TwitchConfig>(
    builder.Configuration.GetSection("Twitch"));

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<RelayService>();

// ── App ───────────────────────────────────────────────────────────────────────

var app = builder.Build();

// WebSocket support
app.UseWebSockets();

// ── Endpoints ─────────────────────────────────────────────────────────────────

// ChaosPlane desktop app connects here and holds a persistent WebSocket
app.MapGet("/relay", async (HttpContext ctx, RelayService relay) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await relay.HandleDesktopConnectionAsync(ws);
});

// Extension frontend posts here when a viewer triggers a failure
app.MapPost("/trigger", async (HttpContext ctx, JwtService jwt, RelayService relay) =>
{
    // Verify the Twitch JWT from the Authorization header
    var token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
    
    var claims = jwt.Verify(token);
    if (claims == null) { ctx.Response.StatusCode = 401; return; }

    var body = await ctx.Request.ReadFromJsonAsync<TriggerRequest>();
    if (body == null)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    await relay.SendTriggerAsync(body);
    ctx.Response.StatusCode = 200;
});

// Health check for Railway
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Catalogue fetch
app.MapGet("/catalogue", async (HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    
    using var http = new HttpClient();
    var json = await http.GetStringAsync(
        "https://quassbutreally.github.io/ChaosPlane/FailureCatalogue.json");
    
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(json);
});

app.Run();