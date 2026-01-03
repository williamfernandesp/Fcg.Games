This project contains an Azure Function (isolated worker) that triggers every 1 minute and calls the API endpoint `/api/games/random`.

Configuration:
- `GAMES_API_BASE` environment variable: base URL of the Games API (default `http://localhost:5000`).
- `SERVICE_TOKEN` environment variable: optional bearer token for protected endpoints.

Run locally:
- Install Azure Functions Core Tools and .NET 8 SDK.
- From `Fcg.Games.Trigger` folder run `func start`.

Deploy to Azure Functions:
- Use `func azure functionapp publish <APP_NAME>` or use GitHub Actions / Azure Pipelines.
