using ChaosPlane.EBS.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.Configure<TwitchConfig>(
    builder.Configuration.GetSection("Twitch"));

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<RelayService>();
builder.Services.AddSingleton<PubSubService>();

builder.Services.AddCors();

// ── App ───────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

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

    if (claims == null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

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

// ChaosPlane posts active failure IDs here after each trigger/reset
// EBS broadcasts them to the extension via Twitch pubsub
app.MapPost("/active-failures", async (HttpContext ctx, PubSubService pubsub) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<ActiveFailuresMessage>();
    if (body == null)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    await pubsub.BroadcastActiveFailuresAsync(body.FailureIds);
    ctx.Response.StatusCode = 200;
});

// Status
app.MapGet("/status", (RelayService relay) =>
    Results.Ok(new { online = relay.IsDesktopConnected }));

app.MapGet("/test-pubsub", async (PubSubService pubsub) =>
{
    await pubsub.BroadcastActiveFailuresAsync(new[] { "test_failure_id" });
    return Results.Ok(new { sent = true });
});

app.Run();