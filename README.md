# SmartScout 🏀📊

**SmartScout** is a high-performance, "Moneyball"-inspired basketball analytics engine and ETL (Extract, Transform, Load) pipeline built with **.NET 10**. It is designed to autonomously scrape, process, and analyze full-career NBA data to identify undervalued talent ("Sleepers") using advanced metrics.

## 🚀 Features

*   **Mass-Import Career Scraping:** Extracts a player's entire career history in a single HTTP request using optimized DOM parsing.
*   **Advanced Analytics:** Calculates and stores crucial modern basketball metrics including **PER** (Player Efficiency Rating), **TS%** (True Shooting), **USG%** (Usage Rate), **WS** (Win Shares), and **BPM** (Box Plus/Minus).
*   **Idempotent Execution:** The background worker acts as a smart engine. It remembers processed entities, allowing the pipeline to be safely paused and resumed without data duplication.
*   **Resilient Network Layer:** Integrates **Polly** for intelligent retry policies, graceful degradation, and strict rate-limit compliance to prevent IP bans.
*   **Trade-Proof Parsing:** Utilizes custom string-split extraction logic that dynamically adapts to mid-season trades and DOM variations without relying on fragile HTML IDs.

## 🏗️ Architecture & Tech Stack

The application strictly follows **Clean Architecture** principles, separating domain logic from external concerns, and implements the **CQRS** (Command Query Responsibility Segregation) pattern.

*   **Framework:** .NET 10 Web API
*   **Database:** PostgreSQL
*   **ORM:** Entity Framework Core
*   **Mediator Pattern:** MediatR
*   **Scraping Engine:** HtmlAgilityPack
*   **Resilience:** Polly

## 🗄️ Database Schema

The database utilizes a highly optimized **1-to-Many** relationship architecture:
*   `Players`: Stores normalized biographical data (Height, Weight, Position, etc.).
*   `SeasonStats`: Stores multiple rows per player representing their complete year-by-year career trajectory, using `numeric` data types for zero-precision-loss advanced metric queries.

## ⚙️ Getting Started

### Prerequisites
*   [.NET 10 SDK](https://dotnet.microsoft.com/)
*   [PostgreSQL](https://www.postgresql.org/)

### Installation & Setup

1. **Clone the repository:**
   ```bash
   git clone [https://github.com/YOUR_USERNAME/SmartScout.git](https://github.com/YOUR_USERNAME/SmartScout.git)
   cd SmartScout
