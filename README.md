# PriceTicker

A small always-on-top desktop widget that shows the prices and charts of a personal list of ETFs,
gold and Bitcoin. Built with **.NET 10 (LTS)** and **Avalonia 11**, so it runs on Windows, macOS and Linux.

- Borderless, semi-transparent window; drag it anywhere, starts in the top-right corner.
- A sparkline chart per row over a selectable range: **1D · 1W · 3M · 6M · 1Y**. The percentage
  is the change over that range (for 1D: since the previous close). For exchange-traded instruments
  1D is the last trading session; for Bitcoin it is the last 24 hours.
- Green / red by direction; hover a name or price to see when the price is from, or a chart to see
  why it is missing.
- If a refresh fails, the last price stays on screen, dimmed, with the reason in the tooltip.
- Optional **holdings**: under each name, the purchase price and the gain since; the row turns
  **yellow** once the review alert (e.g. −20%) is reached and **red** at a sell alert (exit at −40%
  or take-profit at +50%). Hover the name to see the alert levels.
- Optional **gold bar panel**: a physical bar's buy and buyback prices at Tavex and igold (read from
  their public product pages), what was paid, and the buyback prices that give the profit targets.
  A buyback price that reaches a target turns green.
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
| Bitcoin `BTC/EUR` | [Twelve Data](https://twelvedata.com) `/quote` and `/time_series` | free | Free plan: 8 credits/minute, 800/day. Each refresh costs one credit for the quote and one for the chart (≈ 576/day at 5 minutes), plus one per range change. Commodities such as `XAU/EUR` need a paid plan. |

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

## Personal data: holdings and the gold bar

Purchase prices, alert levels and the gold bar are personal, so they live in
`src/PriceTicker.App/appsettings.Local.json`, which is git-ignored and copied next to the exe on
build. Start from `appsettings.Local.example.json`:

```jsonc
{
  "Ticker": {
    "Holdings": {
      // Keyed by instrument Id. Prices exclude commissions; copy the alert levels from IBKR.
      "smh": { "PurchasePrice": 100.00, "ReviewAtOrBelow": 80.00, "SellAtOrBelow": 60.00, "SellAtOrAbove": 150.00 }
    }
  },
  "GoldBar": { "Name": "Gold bar · Valcambi 10 g", "PurchasePrice": 1000.00, "ProfitTargetsPercent": [5, 7] }
}
```

Alert levels are copied, not synced: when you change an alert in IBKR, update this file. Without a
`GoldBar` section the panel is hidden. The dealer pages are not an API; if a site is redesigned the
row says "Prices not found on page." instead of showing a wrong number.

## Architecture

```
src/
  PriceTicker.Core            Domain: Instrument, Quote, PriceHistory; provider interfaces; quote and
                              history services sharing one ProviderRouter
  PriceTicker.Infrastructure  Yahoo and Twelve Data providers (quotes + history), DI registration
  PriceTicker.App             Avalonia UI: MVVM view models, Sparkline control, composition root
tests/
  PriceTicker.Tests           xUnit: providers with stubbed HTTP, routing, view models, validation
```

- Dependencies point inward: App → Infrastructure → Core. Core has no UI or HTTP dependencies.
- A missing price is a normal outcome, modelled as `QuoteUnavailable` rather than an exception.
- Time comes from `TimeProvider`, so the refresh loop is tested with `FakeTimeProvider`.

## Adding a provider

1. Implement `IQuoteProvider` (and `IPriceHistoryProvider` for charts) in Infrastructure: return one
   result per instrument and don't throw for data errors.
2. Register it in `ServiceCollectionExtensions.AddPriceTickerQuotes` under each interface it implements.
3. Use its `Key` as `Provider` in `appsettings.json`.

## Disclaimer

For personal information only. Prices may be delayed or wrong; not investment advice.
