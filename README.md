# ReFuel Macedonia

**ReFuel — Your intelligent fuel companion.**
Fuel Delivery, fuel prices, stations, routes and smarter decisions — all in one place.

ASP.NET Core 10 MVC · Entity Framework Core · SQLite · Leaflet / OpenStreetMap · Chart.js

---

## Run it

1. Open `ReFuel Macedonia.slnx` in Visual Studio.
2. Press **F5**.

The SQLite database `refuel.db` is created and seeded automatically on first run. A few seconds later the background worker imports the real gas-station network from OpenStreetMap, the regulated prices from the Energy Regulatory Commission, the weather from Open-Meteo and the road conditions from AMSM.

To start from scratch: stop the app, delete `refuel.db`, run again.

### Accounts

| Role | E-mail | Password |
|---|---|---|
| Administrator | `admin@refuel.mk` | `Admin123!` |
| Customer | `demo@refuel.mk` | `Demo123!` |
| Business (Транс Логистик) | `biznis@refuel.mk` | `Biznis123!` |
| Other customers | `ana@refuel.mk`, `besnik@refuel.mk`, `elena@refuel.mk` | `Demo123!` |

---

## Modules

| Area | What it does |
|---|---|
| **Delivery** | Standard, scheduled and emergency (roadside SOS) fuel delivery. Quick-order widget on the home page, map address picker, live price breakdown, automatic driver assignment. |
| **Live tracking** | Driver profile with photo, rating, truck and plate; animated map with the truck position; ETA countdown; progress steps; JSON endpoint polled every 20 s. |
| **Prices** | National price board from the ERC, 42 months of weekly history, per-station price matrix with history, brand and city comparison, crowd-sourced price reports with an anomaly filter. |
| **Stations** | ~400 stations imported from OpenStreetMap. Search with Cyrillic/Latin transliteration, filters, ratings, favourites, clustered country-wide map. |
| **Weather & roads** | Live weather for 14 cities (Open-Meteo), road conditions and border-crossing waiting times (AMSM), driving advice, conditions map. |
| **Business (B2B)** | 12-month supply contracts, fleet/boat/machinery assets, automatically generated nightly delivery schedules, digital proof of delivery with SHA-256 verification hash, monthly VAT invoices. |
| **AI** | Price forecasting (Holt + ridge AR, back-tested), personalised station recommendation, delivery route optimisation (nearest neighbour + 2-opt), savings calculator. |
| **Pricing engine** | "Pump price, delivered." ReFuel buys at the regulator's wholesale price (pump − procurement spread) and quotes every customer at or below the pump price; the spread is the fuel margin, and the tiers decide how much of it is handed back (individuals pay the pump price, fleets pay less). Delivery is priced separately — today / scheduled window (cheaper, grouped) / express — and costed as a share of a route, not a taxi ride per order. Configurable tiers, per-tier volume bands with a margin floor, delivery zones and operating-cost assumptions. Every order carries a transparent "why this price" breakdown and an internal wholesale cost / gross fuel margin / delivery cost / **gross contribution** view. Nothing is hardcoded. |
| **Dispatch** | Priority queue (Emergency › Scheduled › Contract fleet › Standard), zone-grouped routes that respect compartment inventory, live tanker map. |
| **Tankers & IoT** | Multi-compartment tankers with per-fuel inventory, telemetry history, `POST /api/iot/tankers/{id}/telemetry` for real hardware, and a simulator that uses the same ingestion path. |
| **Fuel alerts** | Volume-variance, meter-mismatch, inventory-drift and pump-while-moving detection, presented as an alert for review — never as an accusation. |
| **ReFuel Economics** | Break-even and profitability model with every assumption visible. Gross contribution is never called net profit. |
| **Company savings** | Total-operating-cost calculator: fuel + lost working time + extra kilometres versus delivered fuel. |
| **Sales proposals** | Fleet offers with estimated monthly revenue, gross fuel margin, delivery cost and gross contribution before they are sent. |
| **Discounts** | Promotions with week-day masks, per-user cool-downs and caps — deliberately not available every day. |
| **Company site** | Landing page built from live data: ERC price ticker, customer segments priced from the pricing tiers, delivery-zone coverage map, live statistics, FAQ. Utility bar with contact details, document verification and language switch; five-column footer with legal pages (Terms of Service, Privacy Policy) rendered in MK/EN. Light and dark theme (`rf_theme` cookie, no flash on load), cookie notice, toast messages. Company identity (name, address, phone, registration and tax numbers) comes from the `Company` section of `appsettings.json`. |
| **Languages** | Macedonian, English, Albanian — 1 326 keys each. |

---

## Structure

```
ReFuel Macedonia/
├── Program.cs                       start-up, DI, localisation, pipeline
├── appsettings.json                 ReFuel section: fees, endpoints, accounts
├── Data/                            DbContext, reference data, seeding, SchemaGuard
├── Models/                          Catalog · Pricing · Users · Ordering · Marketing
│                                    Business (contracts, POD, invoices) · Conditions (weather, roads)
│                                    Fleet (tiers, bands, zones, compartments, telemetry, sites,
│                                    schedules, alerts, proposals)
├── Models/ViewModels/               view models
├── Services/                        Price · Pricing · Station · Order · Promotion · Notification
│                                    Tracking · Business · Fleet · Dispatch
│   ├── Ai/                          PricePrediction · Recommendation · RouteOptimization
│   └── Ingestion/                   OsmImport · ErcPriceScraper · Weather · RoadCondition
│                                    DataRefreshBackgroundService · TelemetrySimulatorService
├── Infrastructure/                  GeoUtils · Translator · Translit · Formatting · Options
├── Controllers/                     Home Stations Prices Orders Account Profile Promotions
│                                    Analytics Conditions Business Dispatch Admin
│                                    Api/PublicApi · Api/Iot
├── Views/                           Razor views + _Icons partial (inline SVG icon set)
├── Resources/                       mk.json · en.json · sq.json
└── wwwroot/                         site.css (own design system) · site.js · img/drivers
```

---

## Data sources

| Data | Source |
|---|---|
| Regulated maximum fuel prices | Energy Regulatory Commission of North Macedonia (РКЕ / ERC) |
| Gas stations | OpenStreetMap via the Overpass API (ODbL) |
| Fuel companies | ERC annual report: Makpetrol ≈128, Lukoil ≈40, OKTA ≈28, Dadi Oil, Supertrejd, Crna Reka Petrol, Detoil, Ompetrol, Pucko Petrol, Javor Sped and others (~390 stations in total) |
| Weather | Open-Meteo (no API key required) |
| Road conditions & border crossings | AMSM (Auto-Moto Association of Macedonia) |
| Per-station prices | company sites, user reports, derived from the regulated maximum |

Verified ERC anchors baked into the seed (MKD, mazut per kg):

| From | BS-95 | BS-98 | Eurodiesel | EL-1 | Mazut M-1 |
|---|---|---|---|---|---|
| 27.05.2026 | 87.50 | 89.50 | 92.00 | 94.00 | 51.144 |
| 08.06.2026 | 86.00 | 88.00 | 87.00 | 89.00 | 47.297 |
| 05.08.2026 | 91.00 | 93.00 | 99.50 | 98.50 | 46.419 |
| 11.08.2026 | 87.00 | 89.00 | 91.50 | 92.50 | 43.538 |

The weekly series covering the 42 months before those anchors is reconstructed and stored with `IsOfficial = false`; the scraper replaces it with published decisions over time. Every history row in the UI is labelled *official* or *reconstructed*.

---

## JSON API

`GET /api/v1/` — `fuels`, `prices`, `prices/{id}/history`, `stations/map`, `stations/near`, `stations/cheapest`, `ai/forecast/{id}`, `ai/recommend`, `ai/routes`, `compare/{id}`, `stats`.
OpenAPI document at `/openapi/v1.json` in Development.

`GET /Orders/Live/{id}` returns the live tracking payload used by the tracking page.
`GET /Dispatch/Live` returns the live tanker positions used by the dispatch map.

### IoT telemetry

```
POST /api/iot/tankers/{id}/telemetry
X-ReFuel-Key: refuel-device-key
Content-Type: application/json

{ "latitude": 41.9981, "longitude": 21.4254, "speedKph": 42,
  "litresOnBoard": 860, "odometerKm": 184220, "pumpActive": false, "engineOn": true }
```

`GET /api/iot/tankers` and `GET /api/iot/tankers/{id}/telemetry` use the same key. The built-in
simulator writes through exactly the same service method, so a real sensor replaces it without a
code change — switch it off with `ReFuel:EnableTelemetrySimulator`.

---

## Notes

* SQLite has no decimal type, so every `decimal` is stored through a provider conversion to `double`; money aggregations are therefore evaluated in memory.
* Localisation uses flat JSON files (`Resources/{culture}.json`) through a small `ITranslator`, injected into every view as `T`.
* The formatting culture is pinned to invariant while the UI culture switches MK/EN/AL, so numbers and coordinates always use a dot.
* `EnsureCreated()` is used instead of migrations so the project runs with a single F5. `SchemaGuard`
  additively repairs the schema on start-up (`CREATE TABLE` / `ALTER TABLE ADD COLUMN` inside a
  transaction), so registered accounts and order history survive a model change. If a repair is
  impossible it rebuilds the file and carries the Identity tables across.
* Pricing lives in `IPricingService`, not in controllers. The procurement spread, tier margins and
  floors, volume bands, zone fees, route-costing parameters and operating costs are all rows in the
  database and are edited under **Administration → Pricing**. `PricingSettings.PricingModelVersion`
  lets the seeder retune an existing database once when the model changes.
* One delivery-cost estimate for the whole application: `DeliveryCosting` (Infrastructure). Standard
  and scheduled orders are a share of a multi-stop route; emergencies and contract drops are a
  dedicated round trip. The quote, the profitability report, the economics calculator and sales
  offers all use it, so they can never disagree.
* Contracts keep the language a customer signs: a fixed discount per litre off the ERC pump price.
  ReFuel's margin is what is left of the spread after that discount; the discount is capped by the
  tier's minimum margin. Each drop records the reference pump price it was priced against.
* The internal figure is **gross contribution** — gross fuel margin plus fees minus delivery cost.
  It is not net profit: salaries, fleet depreciation and administration are not deducted.
* Station geometry originates from OpenStreetMap and is available under the ODbL; attribution is rendered on every map.
