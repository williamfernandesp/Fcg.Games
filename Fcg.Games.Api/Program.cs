using Fcg.Games.Api.Data;
using Fcg.Games.Api.Models;
using Fcg.Games.Api.Repositories;
using Fcg.Games.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Bind ElasticSettings and register client
builder.Services.Configure<ElasticSettings>(builder.Configuration.GetSection("ElasticSettings"));
var esSettings = builder.Configuration.GetSection("ElasticSettings").Get<ElasticSettings>() ?? new ElasticSettings();
builder.Services.AddSingleton(esSettings);
builder.Services.AddSingleton<ElasticClientService>();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Fcg.Games API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and your token"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            }, new string[] { }
        }
    });
});

// Allow disabling real JWT for local testing (set DisableJwt=true in appsettings or environment)
var disableJwt = builder.Configuration.GetValue<bool>("DisableJwt");

// JWT defaults
var jwtKey = builder.Configuration["Jwt:Key"] ?? "very-strong-default-key-change-me";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "fcg";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "fcg-audience";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

if (!disableJwt)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    }).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey
        };
    });

    // Require authentication by default
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });
}
else
{
    // Register a simple test auth scheme that authenticates every request as Admin for local testing
    builder.Services.AddAuthentication("TestScheme")
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });

    builder.Services.AddAuthorization(options =>
    {
        // Fallback allows authenticated user (our test handler will always succeed)
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });
}

// Configure Postgres: prefer DefaultConnection (Neon/Azure) then GamesConnection, env var POSTGRES_CONNECTION, then local fallback
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("GamesConnection")
    ?? builder.Configuration["POSTGRES_CONNECTION"]
    ?? "Host=localhost;Database=games;Username=postgres;Password=postgres";

builder.Services.AddDbContext<GamesDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.EnableRetryOnFailure())
);

// Add repositories and clients
builder.Services.AddScoped<GameRepository>();
builder.Services.AddScoped<GenreRepository>();
builder.Services.AddScoped<PromotionRepository>();

// Service token (for future internal calls via API Gateway, keep config)
var serviceToken = builder.Configuration["ServiceToken"] ?? jwtKey;

// Dummy external clients (no external projects referenced)
builder.Services.AddHttpClient<PaymentClient>(client => {
    client.BaseAddress = new Uri(builder.Configuration["PaymentsBaseUrl"] ?? "http://localhost:5002");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
});

builder.Services.AddHttpClient<UserClient>(client => {
    client.BaseAddress = new Uri(builder.Configuration["UsersBaseUrl"] ?? "http://localhost:5001");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
});

var app = builder.Build();

// Apply migrations at startup (recommended for managed DB like Neon during early stages)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Fcg.Games API v1"));
}

app.UseAuthentication();
app.UseAuthorization();


// Games-specific health endpoint following the same pattern as other /api/games endpoints
app.MapGet("/api/games/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();

app.MapGet("/api/games/{id}", async (Guid id, GameRepository repo) =>
{
    var (game, promo) = await repo.GetByIdWithPromotionAsync(id);
    if (game is null) return Results.NotFound();

    var dto = new {
        game.Id,
        game.Title,
        game.Description,
        Price = game.Price,
        Genre = game.Genre,
        Promotion = promo is null ? null : new { promo.Id, promo.DiscountPercentage, promo.StartDate, promo.EndDate, IsActive = promo.IsActive, DiscountedPrice = Math.Round(game.Price * (1 - promo.DiscountPercentage / 100), 2) }
    };

    return Results.Ok(dto);
});

// Return a completely random game (same shape as /api/games/{id})
app.MapGet("/api/games/random", async (GameRepository repo) =>
{
    var games = (await repo.GetAllAsync()).ToList();
    if (!games.Any()) return Results.NotFound();

    var idx = Random.Shared.Next(games.Count);
    var selected = games[idx];

    var (game, promo) = await repo.GetByIdWithPromotionAsync(selected.Id);
    if (game is null) return Results.NotFound();

    var dto = new {
        game.Id,
        game.Title,
        game.Description,
        Price = game.Price,
        Genre = game.Genre,
        Promotion = promo is null ? null : new { promo.Id, promo.DiscountPercentage, promo.StartDate, promo.EndDate, IsActive = promo.IsActive, DiscountedPrice = Math.Round(game.Price * (1 - promo.DiscountPercentage / 100), 2) }
    };

    return Results.Ok(dto);
}).AllowAnonymous();

// Get multiple games by ids: repeat `gameIds` query parameter for each id
app.MapGet("/api/games/ids", async ([FromQuery(Name = "gameIds")] Guid[] gameIds, GameRepository repo, PromotionRepository promoRepo) =>
{
    if (gameIds == null || gameIds.Length == 0)
        return Results.BadRequest(new { Message = "Query parameter 'gameIds' is required (repeatable). Example: ?gameIds={guid}&gameIds={guid}" });

    var games = (await repo.GetGamesByIdsAsync(gameIds)).ToList();
    if (!games.Any()) return Results.NotFound();

    var promos = (await promoRepo.GetActivePromotionsForGamesAsync(games.Select(g => g.Id))).ToList();

    var list = games.Select(g => {
        var p = promos.FirstOrDefault(x => x.GameId == g.Id);
        return new
        {
            g.Id,
            g.Title,
            g.Description,
            Price = g.Price,
            Genre = g.Genre,
            Promotion = p is null ? null : new { p.Id, p.DiscountPercentage, p.StartDate, p.EndDate, IsActive = p.IsActive, DiscountedPrice = Math.Round(g.Price * (1 - p.DiscountPercentage / 100), 2) }
        };
    });

    return Results.Ok(list);
}).AllowAnonymous();

app.MapGet("/api/games", async (GameRepository repo, PromotionRepository promoRepo) =>
{
    var games = (await repo.GetAllAsync()).ToList();
    var promos = (await promoRepo.GetActivePromotionsForGamesAsync(games.Select(g => g.Id))).ToList();

    var list = games.Select(g => {
        var p = promos.FirstOrDefault(x => x.GameId == g.Id);
        return new {
            g.Id,
            g.Title,
            g.Description,
            Price = g.Price,
            Genre = g.Genre,
            Promotion = p is null ? null : new { p.Id, p.DiscountPercentage, p.StartDate, p.EndDate, IsActive = p.IsActive, DiscountedPrice = Math.Round(g.Price * (1 - p.DiscountPercentage / 100), 2) }
        };
    });

    return Results.Ok(list);
});

// Search endpoint using Elastic (supports fuzzy matching and optional genre filter)
app.MapGet("/api/games/search", async (string name, int? genre, GameRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(name) && !genre.HasValue) return Results.BadRequest(new { Message = "Query parameter 'name' or 'genre' is required" });

    var results = await repo.SearchAsync(name, genre);
    return Results.Ok(results);
}).AllowAnonymous();

// Suggest endpoint: receives ?genre=ID and returns 5 random suggestions
app.MapGet("/api/games/suggest", async (int genre, GameRepository repo) =>
{
    var suggestions = await repo.RecommendByGenreAsync(genre, 5);
    return Results.Ok(suggestions);
}).RequireAuthorization();

app.MapPost("/api/games", async (CreateGameRequest req, GameRepository repo) =>
{
    var game = new Game { Id = Guid.NewGuid(), Title = req.Title, Description = req.Description, Price = req.Price, Genre = req.Genre };
    var created = await repo.CreateAsync(game);
    return Results.Created($"/api/games/{created.Id}", created);
}).RequireAuthorization();

app.MapDelete("/api/games/{id}", async (Guid id, GameRepository repo) =>
{
    var deleted = await repo.DeleteAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("Admin").Build());

// Genres endpoints
app.MapPost("/api/genres", async (CreateGenreRequest req, GenreRepository repo) =>
{
    if (req.Id <= 0) return Results.BadRequest(new { Message = "Id is required and must be a positive integer." });
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { Message = "Name is required" });

    var existing = await repo.GetByIdAsync(req.Id);
    if (existing is not null)
        return Results.Conflict(new { Message = "Genre with the specified Id already exists" });

    var genre = new Genre { Id = req.Id, Name = req.Name.Trim() };
    var created = await repo.CreateAsync(genre);
    return Results.Created($"/api/genres/{created.Id}", created);
}).RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("Admin").Build());

app.MapGet("/api/genres/{id}", async (int id, GenreRepository repo) =>
{
    var genre = await repo.GetByIdAsync(id);
    return genre is not null ? Results.Ok(genre) : Results.NotFound();
}).AllowAnonymous();

app.MapGet("/api/genres", async (GenreRepository repo) => Results.Ok(await repo.GetAllAsync())).AllowAnonymous();

app.MapDelete("/api/genres/{id}", async (int id, GenreRepository repo) =>
{
    var deleted = await repo.DeleteAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("Admin").Build());

// Promotions
app.MapPost("/api/promotions", async (CreatePromotionRequest req, PromotionRepository repo) =>
{
    if (req.GameId == Guid.Empty) return Results.BadRequest(new { Message = "GameId é requerido" });
    if (req.DiscountPercentage <= 0 || req.DiscountPercentage >= 100) return Results.BadRequest(new { Message = "Procentagem de desconto deve ser entre 0 e 100" });
    if (req.EndDate <= req.StartDate) return Results.BadRequest(new { Message = "Data final deve ser depois de data inicial" });

    var promo = new Promotion { Id = Guid.NewGuid(), GameId = req.GameId, DiscountPercentage = req.DiscountPercentage, StartDate = req.StartDate.ToUniversalTime(), EndDate = req.EndDate.ToUniversalTime() };
    var created = await repo.CreateAsync(promo);
    return Results.Created($"/api/promotions/{created.Id}", created);
}).RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("Admin").Build());

app.MapGet("/api/promotions/{id}", async (Guid id, PromotionRepository repo) =>
{
    var p = await repo.GetByIdAsync(id);
    return p is not null ? Results.Ok(p) : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/api/promotions", async (PromotionRepository repo) => Results.Ok(await repo.GetAllAsync())).RequireAuthorization();

app.MapDelete("/api/promotions/{id}", async (Guid id, PromotionRepository repo) =>
{
    var deleted = await repo.DeleteAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("Admin").Build());

// Top searched games endpoint (aggregated from Elastic search-hits index)
app.MapGet("/api/games/top-searched", async (int? size, GameRepository repo) =>
{
    var s = size ?? 10;
    var results = await repo.GetTopSearchedAsync(s);
    return Results.Ok(results);
}).RequireAuthorization();

// Admin endpoint: reindex all games from Postgres into Elasticsearch
app.MapPost("/api/admin/reindex-elastic", async (GameRepository repo, Fcg.Games.Api.Services.ElasticClientService elastic, ILogger<Program> logger) =>
{
    var games = (await repo.GetAllAsync()).ToList();
    if (!games.Any()) return Results.Ok(new { Reindexed = 0, Message = "No games found" });

    var success = 0;
    foreach (var g in games)
    {
        try
        {
            await elastic.IndexGameAsync(g);
            success++;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to index game {Id}", g.Id);
        }
    }

    return Results.Ok(new { Reindexed = success, Total = games.Count });
}).RequireAuthorization(new AuthorizationPolicyBuilder().RequireRole("Admin").Build());

app.MapControllers();

app.Run();

// Records used by API
record CreateGameRequest(string Title, string Description, decimal Price, int Genre);
record BuyRequest(Guid UserId, IEnumerable<Guid> GamesIds);
record CreateGenreRequest(int Id, string Name);
record CreatePromotionRequest(Guid GameId, decimal DiscountPercentage, DateTime StartDate, DateTime EndDate);

// Simple test auth handler used when DisableJwt=true to authenticate all requests as Admin
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "test-user"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
