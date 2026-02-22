# ChaosPlane

A Twitch channel points integration for X-Plane 12 that lets viewers trigger failures on the CL650 (Challenger 650) during live streams.

Viewers redeem channel point rewards to inflict failures on the aircraft — blocked pitot tubes, hydraulic faults, electrical failures, and more. The host can also trigger failures manually and reset them from the dashboard.

---

## Features

- **Four reward tiers** — Minor, Moderate, Severe, and Pick Your Poison (viewer names a specific failure)
- **271 failures** across 17 ATA chapters, sourced from the CL650 failure catalogue
- **Live dashboard** — active failures with per-failure reset, rolling redemption log, manual trigger
- **Fuzzy matching** for Pick Your Poison — handles typos and partial names
- **Auto-refund** if X-Plane is unreachable or no failures are configured for a tier
- **Periodic ping** to X-Plane every 30 seconds — title bar indicator reflects live status
- **Persistent settings** — Twitch token, reward configuration, and X-Plane connection saved locally

---

## Requirements

- Windows 10 (1809) or later
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Windows App SDK 1.5 Runtime](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- X-Plane 12.1.1 or later (REST API enabled by default)
- A Twitch account with channel points enabled

---

## Building from source

Requires Visual Studio 2022 (or Rider) with the **Windows application development** workload.

```
git clone https://github.com/quassbutreally/chaosplane.git
cd chaosplane
```

Before building, create `ChaosPlane/Secrets.cs` from the template:

```
copy ChaosPlane\Secrets.template.cs ChaosPlane\Secrets.cs
```

Then open `Secrets.cs` and fill in your Twitch Client ID from [dev.twitch.tv/console](https://dev.twitch.tv/console). The redirect URI for your app should be `http://localhost:7842/chaosplane/callback`.

```
dotnet build
```

---

## Architecture

```
App.xaml.cs          — Service construction and lifetime
MainViewModel        — Top-level state, connection lifecycle, commands
DashboardViewModel   — Active failures, log, manual trigger
FailureBrowserViewModel — Failure catalogue browsing and tier assignment
SettingsViewModel    — Connection config and reward setup

FailureOrchestrator  — Coordinates Twitch events → X-Plane writes
TwitchService        — EventSub WebSocket, channel point redemptions, chat
XPlaneService        — REST API client, dataref name→ID resolution, periodic ping
CatalogueService     — Loads and resolves FailureCatalogue.json
FailureConfigService — Persists per-failure tier assignments
SettingsService      — Loads/saves appsettings.json
LocalOAuthListener   — Local HTTP server for OAuth redirect capture
```

---

## Failure catalogue

The `Data/FailureCatalogue.json` file contains 271 failures across 17 ATA chapters. Browse them at [quassbutreally.github.io/chaosplane](https://quassbutreally.github.io/ChaosPlane/).

| Chapter | System |
|---------|--------|
| ATA 21 | Air Conditioning |
| ATA 22 | Auto Flight |
| ATA 23 | Communications |
| ATA 24 | Electrical Power |
| ATA 26 | Fire Protection |
| ATA 27 | Flight Controls |
| ATA 28 | Fuel |
| ATA 29 | Hydraulic Power |
| ATA 30 | Ice & Rain Protection |
| ATA 31 | Instruments |
| ATA 32 | Landing Gear |
| ATA 34 | Navigation |
| ATA 35 | Oxygen |
| ATA 36 | Pneumatic |
| ATA 49 | APU |
| ATA 72–80 | Engine |

---

## License

MIT
