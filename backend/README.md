# Baytak API

Backend for the Baytak mobile app: phone-number login, buildings, apartments, receipts, payments, push
notifications and the personal wallet.

* **Runtime:** .NET 10 (LTS), Azure Functions v4 (isolated worker, HTTP triggers, ASP.NET Core integration)
* **Data:** Entity Framework Core 10. Azure SQL in production, SQLite for local development and tests
* **Auth:** SMS one-time code → JWT (HS256). Role and approval are re-read from the database on every request
* **SMS:** Twilio (REST). **Push:** Expo push service

```
backend/
  src/Baytak.Api/          the Functions app
    Functions/             one class per area (Auth, User, Building, Apartment, Receipt, Wallet)
    Auth/                  JWT issuing/validation, OTP service, SMS senders
    Data/                  EF Core model + migrations (SQL Server)
    Domain/                validation rules (wallet entries, phone numbers, text)
    Services/              Expo push sender
    Common/                response helpers, auth guard, base class
  tests/Baytak.Api.Tests/  xUnit: runs the real handlers against in-memory SQLite
```

## How the app maps to the API

The app calls these routes under `/api` (`Function` = needs the `?code=` function key; the mobile app adds it).

| Area | Method + route | Access | Notes |
| --- | --- | --- | --- |
| Auth | `POST auth/login` | anonymous | `{ phoneNumber, status, long, lat }` – sends an SMS code. `status: "request-admin"` asks for administrator access. Also used for "resend" |
| | `POST auth/validate` | anonymous | `{ phoneNumber, pincode }` → `{ auth: <jwt>, userStatus }` |
| User | `GET user/status` | signed in | `{ userStatus, role }` |
| | `GET user/get/profile`, `POST user/save/profile` | signed in | |
| | `POST setup/notification` | signed in | `{ notificationToken }` (Expo push token). A token belongs to one account at a time |
| | `POST user/logout` | signed in | Revokes every token issued to the account and stops push notifications |
| | `POST user/delete` | signed in | Deletes the account and its personal data (admins must hand over a building that has residents first) |
| Buildings | `GET buildings/get` | signed in | `[{ label, value, latitude, longitude }]` |
| | `GET building/get`, `POST building/save` | admin | One building per administrator. Locked once apartments joined |
| Apartments | `GET appartment/get`, `POST appartment/request` | signed in | A resident has one apartment: `Pending → Approved / Rejected` (rejected can re-request) |
| | `GET appartment/pending/get`, `GET appartment/approved/get` | admin | `"NoAppartments"` when empty |
| | `POST appartment/approve`, `appartment/reject` | admin | `{ id }`; the resident gets a push notification |
| | `POST appartment/delete/{id}` | admin | Removes the apartment and its receipts |
| Receipts | `GET get-receipt-format`, `POST save-receipt-format` | admin | Markers: `"SetupBuilding"`, `"SetupReceipt"` |
| | `POST receipt/add` | admin | Total is recomputed on the server. Empty `appartmentIds` = every approved apartment |
| | `GET receipts/appartment/{id}`, `POST receipt/appartment/save-status` | admin | Paid / Pending per apartment |
| | `GET appartment/receipt` (latest 5), `GET appartment/receipts` (all), `GET receipt/history` | signed in | `"Register Apaprtment"` (legacy spelling) until the apartment is approved |
| Wallet | `GET wallet`, `POST wallet/add`, `POST wallet/update/{id}`, `POST wallet/delete/{id}` | signed in | Personal ledger, USD + LBP balances. Add is idempotent when an `id` is sent |

Conventions (kept from the original API because the app depends on them): errors are **plain text** bodies with
a 4xx/5xx status; successes are JSON; mutations are `POST`; the "Appartment" spelling is part of the public routes.

An **administrator** is an account with role `Admin` *and* status `Approved`. Someone who taps "Request building
administration" becomes `Admin / Pending` and can use the app as a resident, but every admin route answers `403`
until an operator approves the account (see below).

## Run it locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the repo targets `net10.0`), [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
(`npm i -g azure-functions-core-tools@4`). Restore the EF tooling pinned in `dotnet-tools.json` with `dotnet tool restore`.

```bash
cd backend/src/Baytak.Api
cp local.settings.sample.json local.settings.json     # SQLite + "Log" SMS provider (codes are printed to the console)
func start
```

The sample settings write verification codes to the log (`[DEV SMS] to +961…: Your Baytak verification code is 123456`)
and create `baytak.dev.db` automatically. `Sms__Provider=Log` is **refused** unless
`AZURE_FUNCTIONS_ENVIRONMENT=Development`, so it can't leak into production by accident.

Point the app at it with `EXPO_PUBLIC_API_BASE_URL=http://<your-LAN-ip>:7071/api` (see the repo's `.env.example`).

### Approving an administrator

New administrator requests wait for an operator. There is no back-office UI yet, so approve with SQL:

```sql
UPDATE Users SET Status = 'Approved' WHERE PhoneNumber = '+9613123456';
```

(Use `'Rejected'` to refuse.) It takes effect on the next request; the app's "Check status" button picks it up.

## Tests

```bash
cd backend
dotnet test
```

68 tests cover login (codes hashed, single use, expiry, attempt/rate limits, SMS failure), token validation
(tampered, expired, foreign key, deleted user), role/approval enforcement, the whole building → apartment →
receipt → payment journey, tenant isolation, and every wallet rule. The handlers run exactly as in Azure, only
without the Functions host.

## Configuration

Set as app settings in Azure (`:` becomes `__`) or in `local.settings.json`.

| Setting | Required | Purpose |
| --- | --- | --- |
| `ConnectionStrings__Baytak` | yes | Azure SQL connection string (SQLite file path when `Database__Provider=Sqlite`) |
| `Database__Provider` | no | `SqlServer` (default) or `Sqlite` |
| `Database__AutoMigrate` | no | Apply EF migrations on startup (default `true`) |
| `Jwt__Key` | yes | HMAC secret, **≥ 32 characters**. Also keys the code hashes. Store in Key Vault |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryDays` | no | Defaults `baytak`, `baytak-app`, `30` |
| `Phone__DefaultCountryCode` | no | Prefix for numbers typed without one (default `+961`) |
| `Sms__Provider` | yes | `Twilio` (default) or `Log` (development only) |
| `Sms__AllowedCountryCodes` | no | Countries that may receive login SMS, default `961`. Comma separated, `*` = all. Blocks SMS-pumping fraud |
| `Sms__Twilio__AccountSid`, `AuthToken`, `From` *or* `MessagingServiceSid` | with Twilio | |
| `Otp__TtlMinutes`, `MaxAttempts`, `ResendCooldownSeconds`, `MaxPerDay` | no | Defaults 5 / 5 / 30 / 10 |
| `Otp__FixedCode` + `Otp__FixedCodePhones` | no | A fixed code for specific test numbers only (e.g. app-store review). No SMS is sent for them. Leave empty in production |
| `Push__Enabled`, `Push__AccessToken` | no | Expo push. The token is only needed if "enhanced push security" is enabled in Expo |
| `AzureWebJobsStorage`, `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` | yes | Standard Functions settings |

## Deploy to Azure

One-time provisioning (names are examples):

```bash
RG=baytak-rg; LOC=westeurope; SUFFIX=$RANDOM
az group create -n $RG -l $LOC

# Storage + Function App (Linux, Flex Consumption, .NET 10 isolated).
# .NET 10 is not available on the old Linux Consumption plan; Flex Consumption is the serverless option for it.
az storage account create -n baytakstore$SUFFIX -g $RG -l $LOC --sku Standard_LRS
az monitor app-insights component create -a baytak-ai -g $RG -l $LOC --kind web
az functionapp create -n baytak-api-$SUFFIX -g $RG --storage-account baytakstore$SUFFIX \
  --flexconsumption-location $LOC --runtime dotnet-isolated --runtime-version 10.0 \
  --app-insights baytak-ai

# Azure SQL (serverless keeps cost low)
az sql server create -n baytak-sql-$SUFFIX -g $RG -l $LOC -u baytakadmin -p '<strong-password>'
az sql db create -g $RG -s baytak-sql-$SUFFIX -n baytak --edition GeneralPurpose --family Gen5 \
  --capacity 1 --compute-model Serverless --auto-pause-delay 60
az sql server firewall-rule create -g $RG -s baytak-sql-$SUFFIX -n AllowAzure \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Transport security: HTTPS only, TLS 1.2+, no FTP
az functionapp update -n baytak-api-$SUFFIX -g $RG --set httpsOnly=true
az functionapp config set -n baytak-api-$SUFFIX -g $RG --min-tls-version 1.2 --ftps-state Disabled

# Settings (use Key Vault references for secrets in production)
az functionapp config appsettings set -n baytak-api-$SUFFIX -g $RG --settings \
  "ConnectionStrings__Baytak=Server=tcp:baytak-sql-$SUFFIX.database.windows.net,1433;Database=baytak;User ID=baytakadmin;Password=<strong-password>;Encrypt=True;" \
  "Jwt__Key=$(openssl rand -base64 48)" "Sms__Provider=Twilio" \
  "Sms__Twilio__AccountSid=..." "Sms__Twilio__AuthToken=..." "Sms__Twilio__From=+1..."
```

Publish (migrations run on first start):

```bash
cd backend/src/Baytak.Api
func azure functionapp publish baytak-api-$SUFFIX
```

Then fetch the host key and configure the app (`EXPO_PUBLIC_API_BASE_URL=https://baytak-api-<suffix>.azurewebsites.net/api`,
`EXPO_PUBLIC_API_KEY=<host key>`):

```bash
az functionapp keys list -n baytak-api-$SUFFIX -g $RG --query functionKeys.default -o tsv
```

> The `.github/workflows/backend.yml` workflow builds and tests on .NET 10 on every push to `backend/**`. Its optional
> deploy job signs in to Azure with OIDC (`azure/login`) and needs the repository variable `AZURE_FUNCTIONAPP_NAME` and
> the secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` (a federated credential for the repo).
> Flex Consumption does not accept publish profiles.

### Upgrading an existing Function App to .NET 10

A Linux **Consumption** app cannot run .NET 10. Create a Flex Consumption app as above, deploy to it, move the app
settings over, point `EXPO_PUBLIC_API_BASE_URL` (and the host key) at the new app, then retire the old one. Premium and
Dedicated plans can change the runtime in place instead.

### .NET 10 upgrade notes

* `net8.0` to `net10.0` in both projects. EF Core (`Design`, `Sqlite`, `SqlServer`) and `Microsoft.Extensions.TimeProvider.Testing` moved to 10.x; the `dotnet-ef` tool is pinned to 10.0.12 to match.
* The Functions packages (`Worker` 2.52, `Worker.Sdk` 2.1) already meet the .NET 10 minimums (2.50 and 2.0.5).
* `System.IdentityModel.Tokens.Jwt` has its own version line (8.x) and stays on 8.
* `Microsoft.ApplicationInsights.WorkerService` stays pinned to 2.23.0: 3.x fails to load with the Functions worker (type-load mismatch).
* No schema change: `dotnet ef migrations has-pending-model-changes` reports nothing pending, and all 68 tests pass on .NET 10.

### Existing data

This API starts with an empty database. If the previous backend still holds users, buildings and receipts, they need
a one-off import (accounts are matched by phone number; receipts are stored per building and apartment, see
`Data/Entities.cs`). Old app sessions are signed with a different key, so everyone signs in once more.

## Security notes

Reviewed against OWASP ASVS / API Security Top 10 and OWASP MASVS (mobile).


* Verification codes: random, 6 digits, HMAC-hashed with the JWT key, 5 minute lifetime, single use, 5 attempts,
  30 s resend cool-down, 10 per number per day.
* JWTs: HS256 only, issuer/audience/expiry enforced, 30 s clock skew. The account is reloaded from the database on
  each request, so removing a user or changing a role is immediate.
* Every query is scoped by the signed-in user (wallet) or by the administrator's own building (admin routes);
  other tenants' ids return `404`.
* Inputs are validated and bounded (lengths, decimals, currency, dates, field counts); money is stored as integers
  (minor units) in the wallet and `decimal(18,2)` for receipts. Receipt totals are always recomputed on the server.
* Sessions: tokens carry a session version; `user/logout` bumps it, so every token issued before is rejected.
  Deleting an account removes its data and invalidates its tokens.
* The admin-request flag is taken only from the request that carries it. It is never inherited from an earlier code,
  so nobody can plant "request-admin" on another person's login (fixed after the security review; regression test
  `SecurityTests.An_attacker_cannot_plant_request_admin_on_someone_elses_login`).
* Login SMS go only to allowed countries (`Sms__AllowedCountryCodes`) to stop SMS-pumping / toll fraud through the
  anonymous login endpoint. Test numbers listed in `Otp__FixedCodePhones` are exempt (they never receive an SMS).
* Logs never contain codes, tokens or full phone numbers (masked); Twilio failures log only Twilio's error code.
* Responses carry `nosniff`, `no-store`, `DENY` framing, a locked-down CSP and HSTS. HSTS only takes effect over
  HTTPS, so enable `httpsOnly` on the Function App (see the deploy steps).
* Login codes older than two days are purged automatically.
* The function key in the app is not a secret (it ships in the app). Real protection is the JWT.
* Next steps worth considering: rotate the old function key that was committed in earlier versions of the app, Key Vault references for secrets, per-IP rate limiting at the edge (Azure Front Door /
  API Management) for `auth/*`, and a back-office page for approving administrators.
