# PriceTicker

A small always-on-top desktop widget that shows the prices of a personal list of ETFs, gold and
Bitcoin. Built with **.NET 10 (LTS)** and **Avalonia 11**, so it runs on Windows, macOS and Linux.

- Borderless, semi-transparent window; drag it anywhere, starts in the top-right corner.
- Green / red change against the previous close; hover a row to see when the price is from.
- If a refresh fails, the last price stays on screen, dimmed, with the reason in the tooltip.
- Lives in the notification area (system tray), not the taskbar. Left-click the tray icon to
  show or hide the widget; right-click it for **Show / hide**, **Refresh now**, **Always on top**,
  **Start with Windows** and **Exit**. The widget's own right-click menu offers the same plus **Hide to tray**.
- **Start with Windows** adds the running `PriceTicker.exe` to the current user's `Run` registry key
  (no admin rights; also visible in Task Manager → Startup apps). Tick it from the published exe,
  not from `dotnet run`, or Windows will start the copy in `bin\Debug`.

## Data sources

| Instruments | Provider | Key | Notes |
|---|---|---|---|
| European ETFs (Xetra `.DE`, Milan `.MI`, Paris `.PA`) and gold via Xetra-Gold `4GLD.DE` (≈ 1 g of gold in EUR) | Yahoo Finance chart endpoint | none | Unofficial and undocumented; personal use only; quotes are typically delayed. |
| Bitcoin `BTC/EUR` | [Twelve Data](https://twelvedata.com) `/quote` | free | Free plan: 8 credits/minute, 800/day. One credit per symbol per refresh. Commodities such as `XAU/EUR` need a paid plan. |

Every provider sits behind `IQuoteProvider`, so a source can be replaced without touching the UI.

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# 1. Get a free API key at https://twelvedata.com and store it outside the repository
dotnet user-secrets set "Providers:TwelveData:ApiKey" "<your-key>" --project src/PriceTicker.App

# 2. Build, test, run
dotnet build
dotnet test
dotnet run --project src/PriceTicker.App
```

Instead of user secrets you can set the environment variable
`PRICETICKER_Providers__TwelveData__ApiKey`. **Never put the key in `appsettings.json`.**

Without a key the ETFs and gold still work; Bitcoin shows "API key not set."

## Configuration

`src/PriceTicker.App/appsettings.json`:

```jsonc
"Ticker": {
  "RefreshInterval": "00:05:00",          // minimum 00:01:00
  "Instruments": [
    { "Id": "sxr8", "DisplayName": "S&P 500 · SXR8", "Symbol": "SXR8.DE", "Provider": "Yahoo" },
    { "Id": "btc",  "DisplayName": "Bitcoin",        "Symbol": "BTC/EUR", "Provider": "TwelveData" }
  ]
}
```

`Symbol` is in the provider's notation. For Yahoo, find it by searching the ISIN on
finance.yahoo.com and pick the listing on your exchange (`.DE` Xetra, `.MI` Borsa Italiana,
`.PA` Euronext Paris). An invalid configuration stops the app at startup with a clear message.

## Architecture

```
src/
  PriceTicker.Core            Domain: Instrument, Quote, QuoteResult; IQuoteProvider; QuoteService (routing)
  PriceTicker.Infrastructure  Yahoo and Twelve Data providers, DI registration (IHttpClientFactory)
  PriceTicker.App             Avalonia UI: MVVM view models, composition root, options validation
tests/
  PriceTicker.Tests           xUnit: providers with stubbed HTTP, routing, view models, validation
```

- Dependencies point inward: App → Infrastructure → Core. Core has no UI or HTTP dependencies.
- A missing price is a normal outcome, modelled as `QuoteUnavailable` rather than an exception.
- Time comes from `TimeProvider`, so the refresh loop is tested with `FakeTimeProvider`.

## Adding a provider

1. Implement `IQuoteProvider` in Infrastructure (return one `QuoteResult` per instrument; don't throw for data errors).
2. Register it in `ServiceCollectionExtensions.AddPriceTickerQuotes`.
3. Use its `Key` as `Provider` in `appsettings.json`.

## Disclaimer

For personal information only. Prices may be delayed or wrong; not investment advice.
