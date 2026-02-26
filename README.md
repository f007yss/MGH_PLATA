# MGH_PLATA

Simulation work-sample for patient flow through a multi-stage clinical process.

## What This Project Does

- Simulates patient movement through these stages:
  - Wait Room
  - Attendant
  - Vitals
  - RN
  - MD
  - Blood
  - Check Out
- Tracks arrival and length of stay (LOS) as patients move through queues.
- Applies role-specific timing and lunch-break constraints.
- Persists completed patient records to SQL Server.

## Tech Stack

- .NET 8 console app
- C#
- `Microsoft.Data.SqlClient`

## Project Structure

- `MGH_PLATA.sln`
- `MGH_PLATA/MGH_PLATA.csproj`
- `MGH_PLATA/Program.cs`
- `.env.example` (safe template)
- `.env` (local only, ignored by git)

## Prerequisites

- .NET SDK 8+
- SQL Server access

## Configuration

Create/update `.env` in the repo root:

```env
MGH_DB_CONNECTION="Server=YOUR_SERVER;Database=YOUR_DATABASE;Trusted_Connection=True;TrustServerCertificate=True;"
```

The app reads this value at startup and uses it for inserts.

## Build

```powershell
dotnet build .\MGH_PLATA\MGH_PLATA.csproj
```

## Run

```powershell
dotnet run --project .\MGH_PLATA\MGH_PLATA.csproj
```

## Notes

- `.env` is excluded from version control via `.gitignore`.
- `.env.example` is committed so collaborators know the required variable name.
- Current simulation settings (for example `simulatedCase`, run count, stage actives) are in `Program.cs`.
