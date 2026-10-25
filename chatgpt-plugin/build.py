"""
Builds the ChatGPT app directory submission package for Baytak: plugin.json, mcp.json and baytak-chatgpt-plugin.zip.
Run:  python chatgpt-plugin/build.py        (standard library only)

What goes where (from OpenAI's "Submit plugins" guide): the listing fields, test cases, countries and release notes live in
plugin.json under extensions.com.openai (the demo video is website/public/demo/baytak-chatgpt-demo.mp4, made by
chatgpt-plugin/demo/make-video.py). Not in the package, entered in the OpenAI dashboard instead: the reviewer sign-in details,
the domain-verification token, the app icon upload and the policy attestations.
"""
import json
import pathlib
import zipfile

root = pathlib.Path(__file__).resolve().parent
SITE = "https://baytakapi8803.z16.web.core.windows.net"

long_description = (
    "Baytak keeps your residential building's fees in one clear place. Your building administrator issues receipts in the Baytak app; "
    "with Baytak in ChatGPT you can ask what you owe, see your receipts in USD and LBP, and check whether each one is paid.\n\n"
    "Building administrators can also ask who has paid a receipt, mark apartments paid or unpaid, send a payment reminder to apartments "
    "that haven't paid, approve or reject residents who ask to join the building, and issue receipts, in USD, LBP or both at once, after reviewing a preview.\n\n"
    "You sign in with the phone number you use in the Baytak app and a 6-digit code. Baytak only shows your own apartment, or your own "
    "building if you are its administrator. Receipts cannot be edited or deleted once issued, and every amount keeps its currency: "
    "USD and LBP are never added together.\n\n"
    "Baytak is for residential buildings in Lebanon and works with an existing Baytak account."
)

plugin = {
    "$schema": "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json",
    "name": "baytak",
    "version": "1.0.0",
    "description": "See what you owe for your building and, as an administrator, issue receipts and track who has paid.",
    "author": {"name": "Zaven Lepedjian"},
    "homepage": SITE + "/",
    "extensions": {
        "com.openai": {
            "interface": {
                "displayName": "Baytak",
                "shortDescription": "Building fees, made clear.",
                "longDescription": long_description,
                "developerName": "Zaven Lepedjian",
                "category": "Productivity",
                "capabilities": [
                    "Check what you owe, per currency (USD and LBP)",
                    "See your receipts and whether each one is paid",
                    "See who has paid a receipt (building administrators)",
                    "Mark apartments paid or unpaid (building administrators)",
                    "Send a payment reminder to unpaid apartments (building administrators)",
                    "Approve or reject join requests (building administrators)",
                    "Issue receipts in USD, LBP or both after reviewing a preview (building administrators)",
                ],
                "websiteURL": SITE + "/",
                "supportURL": SITE + "/support/",
                "privacyPolicyURL": SITE + "/privacy/",
                "termsOfServiceURL": SITE + "/terms/",
                "defaultPrompt": [
                    "Do I owe anything for my building this month?",
                    "Who hasn't paid this month's building receipt yet?",
                    "Show my last receipts and which ones are paid.",
                ],
                "brandColor": "#00847C",
                "brandColorDark": "#009898",
                "logo": "./assets/logo.png",
                "composerIcon": "./assets/logo.png",
            },
            "review": {
                "test_cases": {
                    "positive": [
                        {
                            "description": "A resident asks what they owe, per currency.",
                            "prompt": "Do I owe anything for my building this month?",
                            "tools_triggered": "get_my_balance",
                            "expected_behavior": "States the amount owed in each currency (USD and LBP kept apart) with each unpaid receipt and its lines.",
                        },
                        {
                            "description": "An administrator checks who has not paid the latest receipt.",
                            "prompt": "Who hasn't paid the latest building receipt?",
                            "tools_triggered": "get_collection_status",
                            "expected_behavior": "Lists the unpaid apartments with resident names, how many apartments have paid, and the amount collected out of the amount expected. No phone numbers are shown.",
                        },
                        {
                            "description": "An administrator marks an apartment paid.",
                            "prompt": "Mark apartment 5A as paid on the latest receipt.",
                            "tools_triggered": "set_payment_status",
                            "expected_behavior": "Replies that apartment 5A was marked paid on the latest receipt. Repeating the request changes nothing further.",
                        },
                        {
                            "description": "An administrator prepares a receipt and sees a preview before anything is issued.",
                            "prompt": "Prepare a receipt for the whole building with maintenance 25 and electricity 40 dollars, and show me the preview first.",
                            "tools_triggered": "get_receipt_format, issue_receipt",
                            "expected_behavior": "Shows a preview (total per apartment, the lines, every apartment, and that it cannot be undone) and issues nothing until the administrator clearly confirms.",
                        },
                        {
                            "description": "An administrator asks who is waiting to join the building.",
                            "prompt": "Who is waiting to join my building?",
                            "tools_triggered": "list_join_requests",
                            "expected_behavior": "Lists the pending join request with its apartment number and floor, and offers to approve or reject it.",
                        },
                    ],
                    "negative": [
                        {
                            "description": "Baytak records fees but takes no payments, so it must not pretend to pay.",
                            "prompt": "Pay my building fees with my credit card.",
                        },
                        {
                            "description": "A resident must not see another apartment's balance.",
                            "prompt": "How much does apartment 4A owe?",
                        },
                        {
                            "description": "Issued receipts cannot be edited or deleted, and no tool does it.",
                            "prompt": "Delete the receipt from October 1.",
                        },
                    ],
                },
                "demo_recording_url": SITE + "/demo/baytak-chatgpt-demo.mp4",
                "commerce": False,
                "commerce_description": "Nothing is bought or sold inside ChatGPT, and Baytak moves no money. It records fees that residents pay their building administrator outside the app.",
            },
            "publication": {"countries": ["LB"], "release_notes": "1.0.0: first release"},
        }
    },
}

mcp = {
    "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
    "mcpServers": {
        "baytak": {
            "type": "streamable-http",
            "url": "https://baytak-mcp-8803.azurewebsites.net/mcp",
            "extensions": {"com.openai": {"auth": {"type": "oauth", "client": {"mode": "cimd"}}}},
        }
    },
}

(root / "plugin.json").write_text(json.dumps(plugin, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
(root / "mcp.json").write_text(json.dumps(mcp, indent=2) + "\n", encoding="utf-8")

# the limits the guide states
ui = plugin["extensions"]["com.openai"]["interface"]
assert len(ui["displayName"]) <= 30 and len(ui["shortDescription"]) <= 30 and len(ui["longDescription"]) <= 4000
assert all(len(p) <= 128 for p in ui["defaultPrompt"]) and len(set(ui["defaultPrompt"])) == len(ui["defaultPrompt"])
cases = plugin["extensions"]["com.openai"]["review"]["test_cases"]
assert len(cases["positive"]) == 5 and len(cases["negative"]) == 3

out = root / "baytak-chatgpt-plugin.zip"
files = [root / "plugin.json", root / "mcp.json", *sorted((root / "assets").glob("*"))]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for f in files:
        z.write(f, f.relative_to(root).as_posix())
print(out.name, [f.relative_to(root).as_posix() for f in files], out.stat().st_size, "bytes")
