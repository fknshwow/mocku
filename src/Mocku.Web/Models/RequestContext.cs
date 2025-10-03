using System.Text.Json;

namespace Mocku.Web.Models;

public class RequestContext
{
    public Dictionary<string, string> Path { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> Query { get; set; } = new();
    public Dictionary<string, object> Body { get; set; } = new();
    public string Method { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string RawBody { get; set; } = string.Empty;

    public static RequestContext FromHttpContext(HttpContext context, Dictionary<string, string> pathParameters, string requestBody)
    {
        var requestContext = new RequestContext
        {
            Path = pathParameters,
            Method = context.Request.Method,
            RequestPath = context.Request.Path.Value ?? "",
            RawBody = requestBody
        };

        // Extract headers
        foreach (var header in context.Request.Headers)
        {
            requestContext.Headers[header.Key.ToLowerInvariant()] = header.Value.ToString();
        }

        // Extract query parameters
        foreach (var query in context.Request.Query)
        {
            requestContext.Query[query.Key] = query.Value.ToString();
        }

        // Parse JSON body if present
        if (!string.IsNullOrEmpty(requestBody))
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(requestBody);
                requestContext.Body = ExtractJsonProperties(jsonElement);
            }
            catch (JsonException)
            {
                // If not valid JSON, store as raw string
                requestContext.Body["_raw"] = requestBody;
            }
        }

        return requestContext;
    }

    private static Dictionary<string, object> ExtractJsonProperties(JsonElement element)
    {
        var result = new Dictionary<string, object>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    result[property.Name] = ExtractJsonValue(property.Value);
                }
                break;
            case JsonValueKind.Array:
                var array = element.EnumerateArray().Select(ExtractJsonValue).ToArray();
                result["_array"] = array;
                break;
            default:
                result["_value"] = ExtractJsonValue(element);
                break;
        }

        return result;
    }

    private static object ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => ExtractJsonProperties(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ExtractJsonValue).ToArray(),
            _ => element.ToString()
        };
    }
}