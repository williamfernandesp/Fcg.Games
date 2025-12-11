using System.Text;

namespace Fcg.Games.Api.Services;

public class ElasticSettings
{
    // These properties are bound from configuration (ElasticSettings section)
    public string ApiKey { get; set; } = string.Empty;
    // CloudId is the Elastic Cloud value (name:base64payload) OR can contain a full URL as fallback
    public string CloudId { get; set; } = string.Empty;
    // Optional explicit base Url (preferred). Example: "https://my-elasticsearch-project-...:443"
    public string Url { get; set; } = string.Empty;

    // Try to derive a base URI from the Url or CloudId (Elastic Cloud format: "name:base64payload")
    public Uri GetBaseUri()
    {
        // Prefer explicit Url when provided
        if (!string.IsNullOrWhiteSpace(Url))
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var explicitUri))
                return explicitUri;
            throw new InvalidOperationException("ElasticSettings.Url is not a valid absolute URI");
        }

        if (string.IsNullOrWhiteSpace(CloudId))
            throw new InvalidOperationException("CloudId or Url must be configured for ElasticSearch");

        // If CloudId contains ':', the part after the first ':' is base64 that decodes to "clusterName$esHost$kibanaHost"
        var parts = CloudId.Split(new[] { ':' }, 2);
        if (parts.Length == 2)
        {
            try
            {
                var payload = parts[1];
                var bytes = Convert.FromBase64String(payload);
                var decoded = Encoding.UTF8.GetString(bytes);
                var segs = decoded.Split('$');
                if (segs.Length >= 2 && !string.IsNullOrWhiteSpace(segs[1]))
                {
                    // ES host may include port and protocol; ensure we use https by default
                    var host = segs[1];
                    if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        host = "https://" + host;
                    return new Uri(host);
                }
            }
            catch
            {
                // fallthrough to fallback
            }
        }

        // Fallback: try to use CloudId directly as URI
        if (Uri.TryCreate(CloudId, UriKind.Absolute, out var uri))
            return uri;

        throw new InvalidOperationException("Unable to derive ElasticSearch URI from CloudId or Url");
    }
}
