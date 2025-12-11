using System.Text.Json;

namespace Fcg.Games.Api.Services;

public record ElasticSearchHit(JsonElement Source, double? Score);
