# Fcg.Games API

Microserviço REST para gerenciar catálogo de jogos, promoções e buscas.

Implementado em .NET 8 (C# 12). Utiliza PostgreSQL como banco primário e Elasticsearch para recursos de busca.

## Visão geral

- CRUD de jogos, gêneros e promoções.
- Busca fuzzy e sugestões via Elasticsearch.
- Swagger para documentação em ambiente de desenvolvimento.

## Principais arquivos

- `Program.cs` — configuração, endpoints e migrações automáticas.
- `Fcg.Games.Api.Data` — contexto EF Core (`GamesDbContext`).
- `Fcg.Games.Api.Repositories` — repositórios de dados.
- `Fcg.Games.Api.Services` — clientes HTTP externos e integração com Elasticsearch.
- `Fcg.Games.Api.Models` — entidades do domínio (ex.: `Game`).

## Executando localmente

1. No diretório do projeto:
   - `dotnet restore`
   - `dotnet run --project Fcg.Games.Api`
2. Em desenvolvimento, o Swagger ficará disponível em `/swagger`.

## Endpoints principais

- `GET /api/games/health` — health check 
- `GET /api/games` — lista todos os jogos
- `GET /api/games/{id}` — obtém jogo por id
- `GET /api/games/random` — jogo aleatório
- `GET /api/games/ids?gameIds={id}&gameIds={id}` — buscar por múltiplos ids
- `POST /api/games` — cria jogo (Authorization)
- `DELETE /api/games/{id}` — remove jogo

- Busca / Sugestões:
  - `GET /api/games/search?name={term}&genre={id}` — busca
  - `GET /api/games/suggest?genre={id}` — 5 sugestões por gênero

- Gêneros:
  - `GET /api/genres`
  - `GET /api/genres/{id}`
  - `POST /api/genres`
  - `DELETE /api/genres/{id}`

- Promoções:
  - `GET /api/promotions`
  - `GET /api/promotions/{id}`
  - `POST /api/promotions`
  - `DELETE /api/promotions/{id}`

- Admin:
  - `POST /api/admin/reindex-elastic` — reindexa todos os jogos no Elasticsearch (Admin)
