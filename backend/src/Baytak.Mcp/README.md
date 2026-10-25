# Baytak MCP server (ChatGPT and Claude connector)

A separate serverless Azure Functions app (.NET 10 isolated, Flex Consumption) that lets people use Baytak from ChatGPT, or
from any other MCP client such as Claude. People sign in with their phone number and the 6-digit SMS code, exactly as in the
app, then ask things like "do I owe anything?" or, as an administrator, "who hasn't paid October's receipt?".

It holds no Baytak data. Every tool calls the Baytak API as the signed-in person, so roles, ownership, receipts being
immutable and server-side totals are enforced in one place, by the API.

## How it works

| Part | What |
| --- | --- |
| Sign-in | An OAuth 2.1 authorization server on top of Baytak's own sign-in: protected resource metadata (RFC 9728), authorization server metadata (RFC 8414), dynamic client registration (RFC 7591) and Client ID Metadata Documents, authorization code with PKCE (S256), rotating refresh tokens. The pages call the API's `auth/login` and `auth/validate`, so the API's SMS limits apply (30 s between codes, 10 a day) |
| Tokens | Access tokens last 1 hour and are bound to `<PublicBaseUrl>/mcp`. They wrap the person's API session, which lasts as long as an app sign-in (30 days). Signing in from ChatGPT does not sign the phone app out; signing out in the app (`user/logout`) ends ChatGPT's access too |
| State | Stateless. Sign-in steps, authorization codes, tokens and registered client ids are sealed with AES-256-GCM (`Mcp:SecretKey`). The only stored rows say which codes and refresh tokens were already used (Azure Table `mcpgrants` in the app's own storage account): codes work once, and a reused refresh token ends the whole session |
| MCP | Streamable HTTP in stateless mode at `/mcp`: each POST is one JSON-RPC message with a JSON reply. Protocol versions 2025-11-25, 2025-06-18 and 2025-03-26 |
| Privacy | Phone numbers and emergency contacts never leave the app. Tools return names, apartment numbers, amounts and receipt notes |

### Endpoints

| Route | Purpose |
| --- | --- |
| `POST /mcp` | MCP endpoint (GET and DELETE answer 405) |
| `GET /.well-known/oauth-protected-resource[/mcp]` | Where to sign in for `/mcp` |
| `GET /.well-known/oauth-authorization-server`, `/.well-known/openid-configuration` | OAuth metadata |
| `POST /oauth/register` | Dynamic client registration |
| `GET`/`POST /oauth/authorize` | The sign-in pages: phone number, SMS code, consent |
| `POST /oauth/token` | Authorization code and refresh token grants |
| `GET /oauth/logo.png` | The app's logo (`assets/adaptive-icon.png`, embedded unchanged) |

### Tools

| Tool | Who | What |
| --- | --- | --- |
| `get_my_balance` | everyone | What I owe now, per currency, with the unpaid receipts and their lines |
| `list_my_receipts` | everyone | My receipts, paid and unpaid |
| `get_my_apartment` | everyone | My apartment, building, request status and role |
| `get_collection_status` | admins | Who paid a receipt, apartment by apartment, collected vs expected |
| `set_payment_status` | admins | Mark apartments paid or unpaid (by door-plate number) |
| `send_payment_reminder` | admins | Push a reminder to unpaid apartments, once a day per receipt |
| `list_join_requests`, `approve_join_request`, `reject_join_request` | admins | Handle join requests |
| `list_apartments` | admins | Approved apartments |
| `get_receipt_format` | admins | Currency and fields of the building's receipts |
| `issue_receipt` | admins | One receipt, or up to 5 in one call (`receipts`), each in USD or LBP (default: the building format's currency), so one call can bill both currencies. Preview first; issues only when called again with `confirm=true` after the administrator agreed. Every receipt is checked before any is issued |

Residents only see the first three; approved administrators see all twelve. Write tools are annotated as such, so ChatGPT
asks before running them. Building setup, the receipt format, the wallet and account deletion stay in the app.

## Settings

| Setting | Required | Notes |
| --- | --- | --- |
| `Mcp__PublicBaseUrl` | yes | This app's public https origin, e.g. `https://<default host name>`. It is the OAuth issuer and the token audience; never taken from request headers |
| `Mcp__SecretKey` | yes | 32 random bytes, base64 (`openssl rand -base64 32`). Rotating it disconnects every client |
| `Mcp__PreviousSecretKey` | no | The old key during a rotation, still accepted for opening sealed values |
| `Mcp__ApiBaseUrl` | yes | The Baytak API, ending in `/api` |
| `Mcp__ApiKey` | yes | The API's Functions host key (sent as `x-functions-key`). Store in Key Vault |
| `Mcp__RedirectHosts` | no | Hosts allowed as OAuth redirect targets and browser origins. Default `chatgpt.com,claude.ai,localhost,127.0.0.1` |
| `Mcp__ClientMetadataHosts` | no | Hosts allowed to serve Client ID Metadata Documents. Default `chatgpt.com,openai.com,claude.ai,anthropic.com` |
| `Mcp__AccessTokenMinutes`, `CodeSeconds`, `SignInMinutes` | no | Defaults 60 / 120 / 15 |
| `Mcp__OpenAiChallenge` | no | The domain-verification token the OpenAI dashboard shows, served at `/.well-known/openai-apps-challenge` (404 while empty) |
| `Mcp__ReplayStore` | no | `Table` (default) or `Memory` (development only) |
| `Mcp__StorageConnection` | no | Storage for the `mcpgrants` table. Defaults to `AzureWebJobsStorage` (connection string or managed identity) |

## Run locally

Run the API first (see [the backend README](../../README.md)), on port 7071. Then:

```bash
cd backend/src/Baytak.Mcp
cp local.settings.sample.json local.settings.json   # set Mcp__SecretKey (openssl rand -base64 32)
dotnet build
cd bin/Debug/net10.0 && func host start --port 7072
```

Older Core Tools (4.0.5907) refuse `func start` in the project folder because they also count the generated
`obj/.../WorkerExtensions.csproj`, and they fail if `local.settings.json` sets `AZURE_FUNCTIONS_ENVIRONMENT` (they set it
themselves). Starting the host from the build output, as above, avoids both.

Try it with the [MCP Inspector](https://github.com/modelcontextprotocol/inspector) (`npx @modelcontextprotocol/inspector`):
transport Streamable HTTP, URL `http://localhost:7072/mcp`. It registers itself, opens the sign-in page, and lists the tools.
With the API's `Sms__Provider=Log`, the code is printed in the API's console.

Tests: `cd backend && dotnet test` runs the MCP tests too. They sign in through the real pages and call every tool against
the real API, hosted in memory.

## Deploy to Azure

Same resource group and naming as the API (see the backend README). Flex Consumption may give the app a unique default host
name, so read it back rather than guessing it.

```bash
RG=baytak-rg; LOC=westeurope; SUFFIX=<the API's suffix>

az storage account create -n baytakmcp$SUFFIX -g $RG -l $LOC --sku Standard_LRS
az functionapp create -n baytak-mcp-$SUFFIX -g $RG --storage-account baytakmcp$SUFFIX \
  --flexconsumption-location $LOC --runtime dotnet-isolated --runtime-version 10.0 --app-insights baytak-ai
az functionapp update -n baytak-mcp-$SUFFIX -g $RG --set httpsOnly=true
az functionapp config set -n baytak-mcp-$SUFFIX -g $RG --min-tls-version 1.2 --ftps-state Disabled

MCP_HOST=$(az functionapp show -n baytak-mcp-$SUFFIX -g $RG --query defaultHostName -o tsv)
API_HOST=$(az functionapp show -n baytak-api-$SUFFIX -g $RG --query defaultHostName -o tsv)
API_KEY=$(az functionapp keys list -n baytak-api-$SUFFIX -g $RG --query functionKeys.default -o tsv)

az functionapp config appsettings set -n baytak-mcp-$SUFFIX -g $RG --settings \
  "Mcp__PublicBaseUrl=https://$MCP_HOST" "Mcp__SecretKey=$(openssl rand -base64 32)" \
  "Mcp__ApiBaseUrl=https://$API_HOST/api" "Mcp__ApiKey=$API_KEY"

cd backend/src/Baytak.Mcp
func azure functionapp publish baytak-mcp-$SUFFIX
```

If the app's storage uses its managed identity instead of a connection string, give that identity the
**Storage Table Data Contributor** role on the storage account. The MCP endpoint is then `https://$MCP_HOST/mcp`.

## Connect it

**ChatGPT, for testing:** turn on developer mode (Settings, Apps and Connectors, Advanced), create a connector with the URL
`https://<MCP host>/mcp` and OAuth authentication. ChatGPT discovers the rest, opens the Baytak sign-in page, and lists the
tools after you allow access. Menu names change; OpenAI's [authentication guide](https://developers.openai.com/apps-sdk/build/auth)
describes what ChatGPT expects.

**Claude:** Settings, Connectors, Add custom connector, with the same URL.

**Everyone on ChatGPT:** submit it to the ChatGPT app directory. OpenAI reviews it and asks for a privacy policy, terms, a
support contact and a way for reviewers to sign in. For the last one, the API's `Otp__FixedCode` and `Otp__FixedCodePhones`
give a review phone number a fixed code with no SMS. The privacy policy should say what tools share with the AI provider
(names, apartment numbers, amounts, receipt notes) and what they don't (phone numbers).

## Security notes

* Redirects are only allowed to the hosts in `Mcp__RedirectHosts`, and the consent page names the host people return to.
  Client ID Metadata Documents are only fetched from `Mcp__ClientMetadataHosts`, over https, with no redirects.
* The sign-in pages are plain forms with no script, a strict Content-Security-Policy and `frame-ancestors 'none'`.
  Their state is sealed, so a page cannot be skipped or altered (for example, jumping to "Allow" without a verified code).
* Browser requests to `/mcp` must come from an allowed Origin (DNS-rebinding protection); ChatGPT's own calls send none.
* Tool output includes text people typed (names, notes). The server's instructions tell the assistant to treat it as data.
  Issuing a receipt always needs a preview and a second, confirmed call.
* Rotate `Mcp__SecretKey` by moving the old value to `Mcp__PreviousSecretKey` first; drop it once connections have been
  re-established (within an hour for access tokens; clients registered under the old key have to reconnect).
