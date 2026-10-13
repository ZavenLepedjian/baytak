# Contributing to Baytak

Thanks for helping. This project is licensed under the [Apache License 2.0](LICENSE).

## Your contributions are under the same license

By submitting a pull request, issue attachment or patch, you agree (as in section 5 of the Apache License) that your
contribution is licensed under the Apache License 2.0, and that you have the right to submit it. If you are
contributing on behalf of an employer, make sure they allow it.

Please do not include code, fonts, images or text you did not write unless its license is compatible with
Apache-2.0, and say where it came from in the pull request.

## Before you open a pull request

1. Open an issue first for anything larger than a small fix, so we can agree on the approach.
2. Branch from `main` and keep the change focused: one topic per pull request.
3. Make the checks pass:

   ```bash
   npx tsc --noEmit                 # app: type-check
   cd backend && dotnet test        # backend: needs the .NET 10 SDK
   ```

4. UI changes follow [DESIGN.md](DESIGN.md) (palette, type, components) and [PRODUCT.md](PRODUCT.md). Include a
   screenshot of the changed screen, at normal and large text size if the layout changed.
5. Never commit secrets: `.env`, `local.settings.json`, keys, tokens or real phone numbers. Use the `*.example` and
   `*.sample.*` files for placeholders.

## Reporting a security problem

Please do not open a public issue for a vulnerability. Contact the maintainer privately through their GitHub profile.
