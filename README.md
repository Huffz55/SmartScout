# SmartScout

**SmartScout** is a Moneyball-inspired global basketball analytics platform, automated ETL pipeline, and scouting workbench built with **.NET 10** and **PostgreSQL**. It ingests multi-season box scores, shooting splits, biographical data, and live injury reports across **15 global basketball leagues** to surface undervalued prospects ("Sleepers") and transferable EuroLeague/NBA role fits.

## Core Capabilities

* **Sleeper & Breakout Engine:** Ranks prospects using **EuroLeague Transition Index (ETI)**, **Pace-Adjusted Impact**, true shooting efficiency, perimeter spacing (`3PM`/`3P%`), defensive disruption (`Stocks/36`), and **Multi-Season Stability**, **Young Prospect (U-24)**, and **Year-over-Year Breakout** filters.
* **Global 15-League Ingestion:** Covers NBA, G-League, NCAA D1, EuroLeague, EuroCup, BCL, BSL (Turkey), ACB (Spain), LNB (France), BBL (Germany), Lega A (Italy), ABA, GBL (Greece), LKL (Lithuania), and VTB with automatic multi-stint (`TOT`/`2TM`) deduplication.
* **Live 8-Feed Injury Tracking:** Syncs active injuries and automatic recovery status from ESPN NBA, Basketball-Reference NBA, Covers NBA, Covers NCAAB, CBS NBA, and Rotowire (NBA / EuroLeague / NCAA).
* **Multi-Source Bio Enrichment:** Resolves missing dates of birth, nationalities, heights, and weights via Wikipedia, Wikidata (`P27` citizenship & `P569` birth date), Basketball-Reference, and EuroLeague v2 feeds.
* **Scout Workbench UI:** Interactive web console (`SmartScout.Api/wwwroot/index.html`) featuring:
  * **Sleepers & Breakouts** and **Smart Finder** natural-language filtering
  * **Player Dossier Modal** with career trajectory charts, medical history, and **QuestPDF** downloadable scouting reports (`/api/players/{id}/report.pdf`)
  * **Head-to-Head Comparison** with dual-overlay radar analytics
  * **Scout Watchlist & BSL 4+3 Foreign Quota Simulator** with CSV export and one-click Demo Target Board seeding
  * **Data Sync Hub** displaying automated background sync cadence, database coverage, and session-scoped `X-Api-Key` entry

## Architecture & Tech Stack

* **Clean Architecture + CQRS:** `SmartScout.Domain`, `SmartScout.Application`, `SmartScout.Infrastructure`, `SmartScout.Api`, `SmartScout.UnitTests`
* **Backend:** .NET 10 Web API, MediatR, Entity Framework Core + Npgsql (PostgreSQL), Polly resilience policies, ASP.NET Core Rate Limiting
* **Scraping & Reporting:** HtmlAgilityPack, Microsoft.Playwright, QuestPDF

## Security & Configuration

No production secrets or database passwords are stored in `SmartScout.Api/appsettings.json`. Supply sensitive settings via environment variables, `.NET User Secrets`, or gitignored `SmartScout.Api/appsettings.Development.json`:

* `ConnectionStrings__DefaultConnection` — PostgreSQL connection string
* `Authentication__ApiKey` (or `SMARTSCOUT_API_KEY`) — Required header (`X-Api-Key`) for admin POST endpoints (`sync`, `sync-stats`, `sync-injuries`, `enrich-bios`); endpoints fail closed (`503`) if unconfigured
* `Database__AutoMigrateOnStartup` — Set to `true` (e.g. in Docker) to run EF Core migrations automatically on boot

See [.env.example](.env.example) for the full variable list.

## Getting Started

### Option 1: Docker Compose (Recommended One-Command Setup)

```bash
cp .env.example .env
# Edit .env with strong POSTGRES_PASSWORD and SMARTSCOUT_API_KEY values
docker compose up -d --build
```

The API + web console will be available at `http://localhost:6050` (health check at `http://localhost:6050/health`).

### Option 2: Local .NET 10 SDK + PostgreSQL

#### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/)
* PostgreSQL 16+

#### Setup

1. Create a local gitignored `SmartScout.Api/appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Database=SmartScoutDb;Username=postgres;Password=YOUR_PASSWORD"
     },
     "Authentication": {
       "ApiKey": "your-local-sync-api-key"
     }
   }
   ```
2. Apply EF Core migrations:
   ```bash
   dotnet ef database update --project SmartScout.Infrastructure --startup-project SmartScout.Api
   ```
3. Run tests and launch the API:
   ```bash
   dotnet test SmartScout.slnx
   dotnet run --project SmartScout.Api
   ```
4. Open `http://localhost:6050` in your browser (Swagger UI available in Development at `/swagger`).
