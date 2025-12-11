using System.Text;

namespace Fcg.Games.Api.Services;

public class ElasticSettings
{
    // These properties are bound from configuration (ElasticSettings section)
    public string ApiKey { get; set; } = string.Empty;
    public string CloudId { get; set; } = string.Empty;

    // Try to derive a base URI from the CloudId (Elastic Cloud format: "name:base64payload")
    public Uri GetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(CloudId))
            throw new InvalidOperationException("CloudId is not configured for ElasticSearch");

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

        throw new InvalidOperationException("Unable to derive ElasticSearch URI from CloudId");
    }
}
