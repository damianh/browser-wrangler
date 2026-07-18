# Browser Wrangler

Browser Wrangler registers itself as a browser on Windows, intercepts the links
you click, and routes them to the right browser and profile based on rules you
define — with an optional picker popup and toast notifications.

It is a .NET 10 / WinUI 3 fork of [Browser Tamer](https://github.com/aloneguid/bt)
by [aloneguid](https://github.com/aloneguid), licensed under the Apache License 2.0.

## Features (MVP)

- **Register as a browser** — per-user registry (no admin), health checks with one-click fixes
- **Browser & profile discovery** — installed browsers, Chromium profiles (Local State), Firefox profiles (profiles.ini), incognito/private entries
- **Rules engine** — substring/regex match on whole URL, domain or path; priority ordering; fallback default profile; bt-compatible rule syntax (`scope:domain|priority:2|github.com`)
- **URL pipeline** — find/replace substitutions (`substr|find|replace`, `rgx|find|replace`)
- **Picker** — popup at cursor listing browsers/profiles; triggered by hotkeys (Ctrl+Shift etc.), rule conflicts, or no-match
- **Toast** — brief notification showing which rule routed to which browser
- **Fast cold-start** — URL invocations route and launch without initializing XAML unless UI is needed

## Building

Requires .NET SDK 10 on Windows.

```pwsh
dotnet build src\BrowserWrangler -p:Platform=x64
dotnet test tests\BrowserWrangler.Core.Tests
```

Run `BrowserWrangler.exe` with no arguments for the config UI; go to the
**Health** page and click **Register as browser**, then set it as the default
browser in Windows Settings.

## Layout

- `src/BrowserWrangler.Core` — models, discovery, rules, pipeline, registry setup, launching (no UI deps)
- `src/BrowserWrangler` — WinUI 3 app: config UI, picker, toast
- `tests/BrowserWrangler.Core.Tests` — xUnit tests

## License

Apache License 2.0 — see [LICENSE](LICENSE). Portions derived from
[Browser Tamer](https://github.com/aloneguid/bt), Copyright aloneguid,
Apache License 2.0 — see [NOTICE](NOTICE).
