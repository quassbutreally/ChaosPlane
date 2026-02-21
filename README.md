# ChaosPlane

A Twitch channel points integration for X-Plane 12 that lets viewers trigger failures on the CL650 (Challenger 650) during live streams.

Viewers redeem channel point rewards to inflict failures on the aircraft — blocked pitot tubes, hydraulic faults, electrical failures, and more. The host can also trigger failures manually and reset them from the dashboard.

![Dashboard screenshot showing active failures and redemption log](.github/screenshot.png)

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
- X-Plane 12.1.1 or later (for the REST API)
- A Twitch account with channel points enabled

---

## Setup

### 1. Twitch application

Create a Twitch application at [dev.twitch.tv/console](https://dev.twitch.tv/console):

- **OAuth Redirect URL:** `http://localhost:7842/chaosplane/callback`
- **Category:** Application Integration

Copy your **Client ID** — you'll need it in the next step.

### 2. Configuration

Copy `ChaosPlane/appsettings.template.json` to `ChaosPlane/appsettings.json` and fill in your Client ID:

```json
{
  "Twitch": {
    "ClientId": "your_client_id_here",
    ...
  }
}
```

Everything else (access token, reward IDs, channel name) is populated automatically through the app.

### 3. X-Plane

Make sure the X-Plane REST API is enabled. It's on by default from 12.1.1 onwards. Check **Settings → Network** and ensure "Disable Incoming Traffic" is not selected.

### 4. First run

1. Launch ChaosPlane
2. Open **Settings** and click **Ping X-Plane** to verify the connection
3. Click **Connect Twitch** and complete the OAuth flow in your browser
4. Click **Push to Twitch** to create the channel point rewards on your channel
5. Open **Failure Browser**, assign tiers to the failures you want to enable, and click **Save**

---

## Building from source

Requires Visual Studio 2022 with the **Windows application development** workload (or VS Build Tools with the WinUI component).

```
git clone https://github.com/yourusername/chaosplane.git
cd chaosplane
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

The `Data/FailureCatalogue.json` file contains 271 failures across 17 ATA chapters:

| Chapter | System |
|---------|--------|
| ATA 21 | Air Conditioning |
| ATA 22 | Auto Flight |
| ATA 23 | Communications |
| ATA 24 | Electrical Power |
| ATA 25 | Equipment / Furnishings |
| ATA 26 | Fire Protection |
| ATA 27 | Flight Controls |
| ATA 28 | Fuel |
| ATA 29 | Hydraulic Power |
| ATA 30 | Ice & Rain Protection |
| ATA 31 | Instruments |
| ATA 32 | Landing Gear |
| ATA 33 | Lights |
| ATA 34 | Navigation |
| ATA 35 | Oxygen |
| ATA 36 | Pneumatic |
| ATA 49 | APU |

Each failure maps to one or more X-Plane datarefs under `CL650/failures/...`.

---

## License

MIT
