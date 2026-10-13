# Baytak

Building fees, made clear. Baytak ("your home" in Arabic) is a mobile app for residential buildings in Lebanon:
the building administrator issues receipts, every resident sees what they owe (in USD and LBP) and whether it is paid,
and nobody has to chase a WhatsApp thread or a paper list. A personal wallet is included as a side tool.

## Stack

| Part | Technology |
| --- | --- |
| Mobile app | Expo SDK 57, React Native 0.86 (new architecture), React 19, React Navigation 7 (bottom tabs + native stack), `expo-dev-client` |
| App services | `expo-secure-store` (session token in the device keychain), `expo-notifications` (push), `expo-font` (IBM Plex Sans Arabic) |
| Backend | .NET 10 (LTS), Azure Functions v4 isolated worker, EF Core 10, Azure SQL (SQLite for local development and tests). See [backend/README.md](backend/README.md) |
| Auth | Phone number plus 6-digit SMS code (Twilio), JWT with a session version so logout revokes every token |
| CI | GitHub Actions: build and test the backend on .NET 10 on every change under `backend/` |

## Repository layout

| Folder | What |
| --- | --- |
| `/` (`App.tsx`, `screens/`, `components/`, `auth/`, `api/`, `hooks/`, `theme.ts`) | The mobile app |
| `components/ui/` | The app's own design-system components (Text, Button, TextField, Card, Screen, Sheet, Toast, Stamp, DoorPlate, ...) |
| `backend/` | .NET 10 Azure Functions API, its tests and its README |
| `PRODUCT.md`, `DESIGN.md` | Who the app is for, and the visual system (palette, type, components, do's and don'ts). Read these before changing the UI |

## Run the app

Prerequisites: Node.js (current LTS) and a development build of the app on a device or emulator
(this project uses `expo-dev-client`, so Expo Go is not enough).

```bash
npm install --legacy-peer-deps
cp .env.example .env      # set EXPO_PUBLIC_API_BASE_URL (and EXPO_PUBLIC_API_KEY)
npx expo start --dev-client
```

If the phone cannot reach your computer over the local network, use a tunnel instead:

```bash
npx expo start --tunnel --dev-client
```

Open the project in the installed dev client (scan the QR code or enter the URL shown). Opening the Metro URL in a
desktop browser shows `Must specify "expo-platform" header or "platform" query parameter`; that is expected, the
address is for the app, not for browsers.

| Variable | Purpose |
| --- | --- |
| `EXPO_PUBLIC_API_BASE_URL` | Base URL of the API, ending in `/api`. Local backend: `http://<your-LAN-ip>:7071/api` |
| `EXPO_PUBLIC_API_KEY` | Azure Functions host key, sent as `?code=`. Not a secret (it ships in the app); the JWT protects the data |

Values prefixed `EXPO_PUBLIC_` are bundled into the app. For EAS builds, set them as EAS environment variables.

Check your work:

```bash
npx tsc --noEmit        # type-check
npx expo-doctor         # validate the Expo project
```

## Run the backend

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Azure Functions Core Tools v4.

```bash
cd backend/src/Baytak.Api
cp local.settings.sample.json local.settings.json    # SQLite, and SMS codes printed to the console
func start
```

Run the tests with `cd backend && dotnet test` (68 tests, runs on .NET 10). Routes, configuration, Azure deployment
(Flex Consumption, because .NET 10 is not available on the Linux Consumption plan) and the security notes are in
[backend/README.md](backend/README.md).

## Upgrading to .NET 10

The backend moved from .NET 8 to .NET 10 (both the API and the test project target `net10.0`):

* EF Core and the `dotnet-ef` tool are on 10.x; no schema change, no new migration.
* CI builds and tests with `dotnet-version: 10.0.x`; the optional deploy job signs in with OIDC.
* An existing Linux Consumption Function App cannot run .NET 10: create a Flex Consumption app, deploy, and point
  `EXPO_PUBLIC_API_BASE_URL` at it. Steps are in the backend README.

## Contributing

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Copyright 2026 Zaven Lepedjian. Licensed under the [Apache License, Version 2.0](LICENSE). You may use, modify and
distribute the software under those terms; it is provided "as is", without warranties. Third-party dependencies and
fonts keep their own licenses.
