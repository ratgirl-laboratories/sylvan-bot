# RatBot

A Discord bot built with .NET 10.0 and Discord.Net, featuring a modular architecture and optional observability.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/get-started) and [Docker Compose](https://docs.docker.com/compose/install/)
- A Discord Bot Token (from the [Discord Developer Portal](https://discord.com/developers/applications))

## Local development

### 1. Create the local configuration

```bash
cp env/local.env.example .env
```

Set `DISCORD__TOKEN` and `DISCORD__GUILDID` in `.env`.

### 2. Start PostgreSQL

```bash
docker compose --env-file .env up -d --wait db
```

The database is exposed at `localhost:5432` using the `DB__*` values in `.env`. RatBot applies pending EF migrations when it starts. To stop it later, run `docker compose down`

### 3. Run or debug the bot

```bash
dotnet run --project RatBot --launch-profile RatBot.Local
```

### Troubleshooting

```bash
docker compose --env-file .env ps
docker compose --env-file .env logs db
```

PostgreSQL only uses `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` when its data volume is first created. If you change those settings after initial startup, either change them inside PostgreSQL or intentionally recreate the local volume with `docker compose down --volumes`.

## Optional: Observability

The project includes an optional observability stack (Grafana, Loki, OpenTelemetry) for monitoring and logging.

### Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `OTEL__Logs__Endpoint` | OpenTelemetry Collector endpoint | (Disabled if empty) |
| `GRAFANA__ADMIN__USER` | Grafana admin username | `admin` |
| `GRAFANA__ADMIN__PASSWORD` | Grafana admin password | `admin` |

To enable logs export, set `OTEL__Logs__Endpoint` to `http://localhost:4317` (if running bot locally) or `http://otel-collector:4317` (if running bot in docker).

### Start Observability Stack

```bash
docker compose --env-file .env up loki grafana otel-collector -d
```

Grafana will be accessible at `http://localhost:3000` (Default: `admin`/`admin`).

## Development

### Project Structure

- `RatBot`: Executable, Discord integration, features, and shared persistence.
- `RatBot.Domain`: Models and rules for features that have not yet been consolidated.

### Running Tests

The infrastructure integration tests use Testcontainers and require access to a running Docker daemon.

```bash
dotnet test
```
