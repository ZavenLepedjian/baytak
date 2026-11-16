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

## WhatsApp assistant

The same twelve tools on WhatsApp, for people who would rather write to a number than open an app. It lives in this app
(folder `WhatsApp/`) and is switched off until every `WhatsApp__` setting below is filled in: until then
`/whatsapp/webhook` answers 404 and nothing else changes.

ChatGPT brings its own language model; WhatsApp does not, so the assistant asks an OpenAI model (through the
Responses API, stateless: no conversation is kept at OpenAI for later calls) what the person means and which tool answers it. WhatsApp is connected directly through Meta's Cloud API, with no provider in between.

| Part | What |
| --- | --- |
| Who is writing | WhatsApp proves the sender's number, and that number is the Baytak account, so there is no sign-in step. The assistant asks the API for a 15-minute session for that number (`auth/service-login`, which needs `Service__Key` and never creates an account). A number without an account is told how to get the app, once a day |
| Reading | Read tools run at once and their answers go to the model, which replies in the person's language. Residents are offered the three resident tools, approved administrators all twelve; the API enforces it either way |
| Changing | The model can only propose a change. The change is checked against the building, then the person sees exactly what would happen with **Confirm** and **Cancel** buttons, in wording made by this app, not by the model. Only their tap (or a plain "yes") carries it out, once, on the receipt that was shown. Buttons expire after 10 minutes |
| Webhook | `GET /whatsapp/webhook` answers Meta's verification. `POST /whatsapp/webhook` accepts only bodies signed with the Meta app secret (`X-Hub-Signature-256`), puts each new message on the `whatsapp-in` queue (sealed) and answers 200 at once; a queue-triggered function does the slow part |
| Memory | The last few turns of a conversation, for 2 hours, in the Azure Table `whatsappchats`: one sealed row per number, keyed by a fingerprint of the number, never the number. A nightly timer deletes rows untouched for 2 days |
| Limits | 40 messages an hour and 200 a day per number (`WhatsApp__MessagesPerHour`, `MessagesPerDay`), so one number cannot run up the model and message bills |
| Not on WhatsApp | Joining a building, building and receipt-format setup, photos, the wallet, handing over the administrator role, account deletion: these stay in the app. Voice notes and photos get a short "text only" notice |

### Settings

| Setting | Notes |
| --- | --- |
| `WhatsApp__VerifyToken` | Any long random text. Typed once into Meta's webhook form |
| `WhatsApp__AppSecret` | The Meta app's secret (App settings, Basic). Signs every webhook body |
| `WhatsApp__AccessToken` | A system-user token with `whatsapp_business_messaging` |
| `WhatsApp__PhoneNumberId` | The business number's id in Meta (digits; not the phone number) |
| `WhatsApp__OpenAiModel` | Default `gpt-6-luna`. It must support the Responses API and tool (function) calling |
| `WhatsApp__OpenAiKey` | An OpenAI API key (a project key that may use that model) |
| `WhatsApp__OpenAiBaseUrl` | Optional, default `https://api.openai.com/v1` |
| `WhatsApp__ApiServiceKey` | The same value as the API's `Service__Key` (32 characters or more). The Functions host key is not enough for signing people in, because it ships inside the mobile app |
| `WhatsApp__ReasoningEffort` | Optional, default `none` (fastest and cheapest). `low` or `medium` read harder requests better but are slower. Set it empty for a model that does not reason and rejects the field (for example GPT-4.1) |
| `WhatsApp__GraphVersion`, `AppUrl`, `MaxReplyTokens`, `HistoryMinutes`, `ConfirmMinutes`, `KeepDays` | Optional. Defaults `v27.0`, the public site, 2000, 120, 10, 2 |

`backend/deploy/setup-whatsapp.ps1` asks for these and stores them on both apps without showing them; it makes the
service key and the verify token itself.

### Set it up

1. **Meta.** In a Meta Business portfolio, create an app at developers.facebook.com for WhatsApp and add a phone number
   that is not registered in the WhatsApp phone app. Create a system user with access to the app and the WhatsApp
   account, and generate a token for it with the two WhatsApp permissions. Note the app secret and the phone number id.
2. **OpenAI.** At platform.openai.com create a project and an API key for it, and check that the project may use the model
   (`gpt-6-luna`). Add a payment method or credit; the key stops working without it.
3. **Deploy** the API and this app (`backend/deploy/deploy-api.ps1`, `deploy-mcp.ps1`), then run `setup-whatsapp.ps1`.
4. **Webhook.** In the Meta app (WhatsApp, Configuration) enter `https://<this app's host>/whatsapp/webhook` and the
   verify token, and subscribe to the `messages` field. Meta calls the GET straight away; it must answer.
5. **Go live.** Add a payment method to the WhatsApp account and switch the Meta app to Live (it asks for the privacy
   policy URL). Until then only numbers you add as testers can write.

Meta bills replies per message after 1,000 free ones a month per number; messages people send are free. Meta allows AI on
WhatsApp only as part of a business's own service, which this is: the assistant declines anything that is not Baytak.

Tests: `WhatsAppTests` plays the model and WhatsApp and runs every flow against the real tools and API;
`WhatsAppClientTests` checks what is sent to OpenAI and to Meta.

## Security notes

* Redirects are only allowed to the hosts in `Mcp__RedirectHosts`, and the consent page names the host people return to.
  Client ID Metadata Documents are only fetched from `Mcp__ClientMetadataHosts`, over https, with no redirects.
* The sign-in pages are plain forms with no script, a strict Content-Security-Policy and `frame-ancestors 'none'`.
  Their state is sealed, so a page cannot be skipped or altered (for example, jumping to "Allow" without a verified code).
* Browser requests to `/mcp` must come from an allowed Origin (DNS-rebinding protection); ChatGPT's own calls send none.
* Tool output includes text people typed (names, notes). The server's instructions tell the assistant to treat it as data.
  Issuing a receipt always needs a preview and a second, confirmed call.
* WhatsApp: a webhook body is trusted only with Meta's signature, and the sender's number only from such a body. What the
  model writes is never trusted with a change: write tools need the person's Confirm, shown with facts the model did not
  write. Message text and phone numbers are never logged; the queue and the stored conversations are sealed.
* Rotate `Mcp__SecretKey` by moving the old value to `Mcp__PreviousSecretKey` first; drop it once connections have been
  re-established (within an hour for access tokens; clients registered under the old key have to reconnect).
