using System.Text.RegularExpressions;
using System.Text.Json;
using Mocku.Web.Models;

namespace Mocku.Web.Services;

public class TemplateProcessor
{
    private readonly ILogger<TemplateProcessor> _logger;
    private static readonly Regex TemplateRegex = new(@"\{\{request\.(path|headers|query|body)\.([^}]+)\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LegacyTemplateRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex FunctionTemplateRegex = new(@"\{\{(toBool|toNumber|toString|toInt|toFloat|toArray|toObject)\((request\.(path|headers|query|body)\.([^)]+)|([^)]+))\)\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TemplateProcessor(ILogger<TemplateProcessor> logger)
    {
        _logger = logger;
    }

    public string ProcessTemplate(string template, RequestContext requestContext)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        var processed = template;

        // Process function-based templates first: {{toBool(request.path.id)}}, {{toNumber(request.body.age)}}, etc.
        processed = FunctionTemplateRegex.Replace(processed, match =>
        {
            var functionName = match.Groups[1].Value.ToLowerInvariant();
            var fullExpression = match.Groups[2].Value;
            
            try
            {
                object? value = null;
                
                // Check if this is a request.* expression
                if (fullExpression.StartsWith("request.", StringComparison.OrdinalIgnoreCase))
                {
                    var requestMatch = Regex.Match(fullExpression, @"request\.(path|headers|query|body)\.(.+)", RegexOptions.IgnoreCase);
                    if (requestMatch.Success)
                    {
                        var source = requestMatch.Groups[1].Value.ToLowerInvariant();
                        var key = requestMatch.Groups[2].Value;
                        
                        value = source switch
                        {
                            "path" => requestContext.Path.GetValueOrDefault(key, null),
                            "headers" => requestContext.Headers.GetValueOrDefault(key.ToLowerInvariant(), null),
                            "query" => requestContext.Query.GetValueOrDefault(key, null),
                            "body" => GetBodyValueAsObject(requestContext.Body, key),
                            _ => null
                        };
                    }
                }
                else
                {
                    // Check if it's a legacy path parameter
                    if (requestContext.Path.TryGetValue(fullExpression, out var pathValue))
                    {
                        value = pathValue;
                    }
                }

                if (value != null)
                {
                    return ConvertValueWithFunction(functionName, value);
                }
                
                return match.Value; // Return original if value not found
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing function template: {Template}", match.Value);
                return match.Value;
            }
        });

        // Process new request context templates: {{request.body.name}}, {{request.headers.authorization}}, etc.
        processed = TemplateRegex.Replace(processed, match =>
        {
            var source = match.Groups[1].Value.ToLowerInvariant(); // path, headers, query, body
            var key = match.Groups[2].Value;

            try
            {
                return source switch
                {
                    "path" => requestContext.Path.GetValueOrDefault(key, $"{{{{request.path.{key}}}}}"),
                    "headers" => requestContext.Headers.GetValueOrDefault(key.ToLowerInvariant(), $"{{{{request.headers.{key}}}}}"),
                    "query" => requestContext.Query.GetValueOrDefault(key, $"{{{{request.query.{key}}}}}"),
                    "body" => GetBodyValueAsString(requestContext.Body, key),
                    _ => match.Value
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing template variable: {Template}", match.Value);
                return match.Value;
            }
        });

        // Process legacy path parameter templates for backward compatibility: {{id}}, {{userId}}, etc.
        processed = LegacyTemplateRegex.Replace(processed, match =>
        {
            var key = match.Groups[1].Value;
            
            // Skip if this is already a request.* template or function call
            if (key.StartsWith("request.", StringComparison.OrdinalIgnoreCase) || 
                key.Contains("(") || key.Contains(")"))
                return match.Value;

            // Try to find in path parameters for backward compatibility
            if (requestContext.Path.TryGetValue(key, out var value))
                return value;

            return match.Value;
        });

        return processed;
    }

    public string ProcessJsonTemplate(string jsonTemplate, RequestContext requestContext)
    {
        if (string.IsNullOrEmpty(jsonTemplate))
            return jsonTemplate;

        try
        {
            // First, try to parse as JSON to validate structure
            var jsonDocument = JsonDocument.Parse(jsonTemplate);
            
            // Process the JSON with template replacement
            var processed = ProcessJsonElement(jsonDocument.RootElement, requestContext);
            
            // Serialize back to JSON string
            return JsonSerializer.Serialize(processed, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            // If not valid JSON, fall back to string template processing
            return ProcessTemplate(jsonTemplate, requestContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing JSON template, falling back to string processing");
            return ProcessTemplate(jsonTemplate, requestContext);
        }
    }

    private object ProcessJsonElement(JsonElement element, RequestContext requestContext)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object>();
                foreach (var property in element.EnumerateObject())
                {
                    obj[property.Name] = ProcessJsonElement(property.Value, requestContext);
                }
                return obj;

            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Select(item => ProcessJsonElement(item, requestContext))
                    .ToArray();

            case JsonValueKind.String:
                var stringValue = element.GetString() ?? "";
                
                // Check if this is a function template that should be replaced with a typed value
                if (IsFunctionTemplate(stringValue))
                {
                    var replacedValue = ProcessFunctionTemplateForTypedValue(stringValue, requestContext);
                    return replacedValue ?? stringValue;
                }
                
                // Check if this is a template variable that should be replaced with a typed value
                if (IsTemplateVariable(stringValue))
                {
                    var replacedValue = ProcessTemplateForTypedValue(stringValue, requestContext);
                    return replacedValue ?? stringValue;
                }
                
                // Otherwise, process as string template
                return ProcessTemplate(stringValue, requestContext);

            case JsonValueKind.Number:
                return element.TryGetInt64(out var longValue) ? longValue : element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null!;

            default:
                return element.ToString();
        }
    }

    private bool IsFunctionTemplate(string value)
    {
        return FunctionTemplateRegex.IsMatch(value) && FunctionTemplateRegex.Matches(value).Count == 1 && 
               FunctionTemplateRegex.Match(value).Value == value;
    }

    private object? ProcessFunctionTemplateForTypedValue(string template, RequestContext requestContext)
    {
        var match = FunctionTemplateRegex.Match(template);
        if (!match.Success)
            return null;

        var functionName = match.Groups[1].Value.ToLowerInvariant();
        var fullExpression = match.Groups[2].Value;
        
        try
        {
            object? value = null;
            
            // Check if this is a request.* expression
            if (fullExpression.StartsWith("request.", StringComparison.OrdinalIgnoreCase))
            {
                var requestMatch = Regex.Match(fullExpression, @"request\.(path|headers|query|body)\.(.+)", RegexOptions.IgnoreCase);
                if (requestMatch.Success)
                {
                    var source = requestMatch.Groups[1].Value.ToLowerInvariant();
                    var key = requestMatch.Groups[2].Value;
                    
                    value = source switch
                    {
                        "path" => requestContext.Path.GetValueOrDefault(key, null),
                        "headers" => requestContext.Headers.GetValueOrDefault(key.ToLowerInvariant(), null),
                        "query" => requestContext.Query.GetValueOrDefault(key, null),
                        "body" => GetBodyValueAsObject(requestContext.Body, key),
                        _ => null
                    };
                }
            }
            else
            {
                // Check if it's a legacy path parameter
                if (requestContext.Path.TryGetValue(fullExpression, out var pathValue))
                {
                    value = pathValue;
                }
            }

            if (value != null)
            {
                return ConvertValueWithFunctionToTyped(functionName, value);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing function template for typed value: {Template}", template);
            return null;
        }
    }

    private string ConvertValueWithFunction(string functionName, object value)
    {
        return ConvertValueWithFunctionToTyped(functionName, value)?.ToString() ?? "";
    }

    private object? ConvertValueWithFunctionToTyped(string functionName, object value)
    {
        try
        {
            return functionName switch
            {
                "tobool" => ConvertToBool(value),
                "tonumber" or "toint" => ConvertToNumber(value),
                "tofloat" => ConvertToFloat(value),
                "tostring" => ConvertToStringValue(value),
                "toarray" => ConvertToArray(value),
                "toobject" => ConvertToObject(value),
                _ => value
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error converting value with function {Function}: {Value}", functionName, value);
            return value;
        }
    }

    private bool ConvertToBool(object value)
    {
        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var result) => result,
            string s when s.Equals("1", StringComparison.OrdinalIgnoreCase) => true,
            string s when s.Equals("0", StringComparison.OrdinalIgnoreCase) => false,
            string s when s.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
            string s when s.Equals("no", StringComparison.OrdinalIgnoreCase) => false,
            string s when s.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
            string s when s.Equals("off", StringComparison.OrdinalIgnoreCase) => false,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0.0,
            _ => false
        };
    }

    private long ConvertToNumber(object value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            float f => (long)f,
            bool b => b ? 1 : 0,
            string s when long.TryParse(s, out var result) => result,
            string s when double.TryParse(s, out var dresult) => (long)dresult,
            _ => 0
        };
    }

    private double ConvertToFloat(object value)
    {
        return value switch
        {
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            bool b => b ? 1.0 : 0.0,
            string s when double.TryParse(s, out var result) => result,
            _ => 0.0
        };
    }

    private string ConvertToStringValue(object? value)
    {
        return value switch
        {
            null => "",
            string str => str,
            bool b => b.ToString().ToLowerInvariant(),
            _ => value.ToString() ?? ""
        };
    }

    private object[] ConvertToArray(object value)
    {
        return value switch
        {
            object[] arr => arr,
            Array arr => arr.Cast<object>().ToArray(),
            string s => s.Split(',').Select(x => (object)x.Trim()).ToArray(),
            _ => new object[] { value }
        };
    }

    private Dictionary<string, object> ConvertToObject(object value)
    {
        return value switch
        {
            Dictionary<string, object> dict => dict,
            string s when TryParseJson(s, out var parsed) => parsed,
            _ => new Dictionary<string, object> { { "value", value } }
        };
    }

    private bool TryParseJson(string json, out Dictionary<string, object> result)
    {
        result = new Dictionary<string, object>();
        try
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                result = ExtractJsonProperties(jsonElement);
                return true;
            }
        }
        catch (JsonException)
        {
            // Not valid JSON
        }
        return false;
    }

    private Dictionary<string, object> ExtractJsonProperties(JsonElement element)
    {
        var result = new Dictionary<string, object>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ExtractJsonValue(property.Value);
        }
        return result;
    }

    private object ExtractJsonValue(JsonElement element)
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

    private bool IsTemplateVariable(string value)
    {
        // Check if the entire string is a single template variable
        return (TemplateRegex.IsMatch(value) && TemplateRegex.Matches(value).Count == 1 && 
               TemplateRegex.Match(value).Value == value) ||
               (LegacyTemplateRegex.IsMatch(value) && LegacyTemplateRegex.Matches(value).Count == 1 &&
               LegacyTemplateRegex.Match(value).Value == value && 
               !value.Contains("(") && !value.Contains(")"));
    }

    private object? ProcessTemplateForTypedValue(string template, RequestContext requestContext)
    {
        var match = TemplateRegex.Match(template);
        if (!match.Success)
        {
            // Try legacy template
            var legacyMatch = LegacyTemplateRegex.Match(template);
            if (legacyMatch.Success && legacyMatch.Value == template)
            {
                var legacyKey = legacyMatch.Groups[1].Value;
                if (requestContext.Path.TryGetValue(legacyKey, out var pathValue))
                {
                    return ConvertToTypedValue(pathValue);
                }
            }
            return null;
        }

        var source = match.Groups[1].Value.ToLowerInvariant();
        var key = match.Groups[2].Value;

        try
        {
            return source switch
            {
                "path" => requestContext.Path.TryGetValue(key, out var pathVal) ? ConvertToTypedValue(pathVal) : null,
                "headers" => requestContext.Headers.TryGetValue(key.ToLowerInvariant(), out var headerVal) ? ConvertToTypedValue(headerVal) : null,
                "query" => requestContext.Query.TryGetValue(key, out var queryVal) ? ConvertToTypedValue(queryVal) : null,
                "body" => GetBodyValueAsTypedValue(requestContext.Body, key),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing typed template variable: {Template}", template);
            return null;
        }
    }

    private object? ConvertToTypedValue(string value)
    {
        // Try to convert string to appropriate type
        if (string.IsNullOrEmpty(value))
            return value;

        // Try boolean
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        // Try integer
        if (long.TryParse(value, out var longValue))
            return longValue;

        // Try decimal
        if (double.TryParse(value, out var doubleValue))
            return doubleValue;

        // Return as string if no conversion possible
        return value;
    }

    private object? GetBodyValueAsTypedValue(Dictionary<string, object> body, string key)
    {
        if (body.TryGetValue(key, out var value))
        {
            return value;
        }

        // Support nested property access with dot notation: user.name, address.city
        var parts = key.Split('.');
        object current = body;

        foreach (var part in parts)
        {
            if (current is Dictionary<string, object> dict && dict.TryGetValue(part, out var nextValue))
            {
                current = nextValue;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private object? GetBodyValueAsObject(Dictionary<string, object> body, string key)
    {
        return GetBodyValueAsTypedValue(body, key);
    }

    private string GetBodyValueAsString(Dictionary<string, object> body, string key)
    {
        if (body.TryGetValue(key, out var value))
        {
            return ConvertToString(value);
        }

        // Support nested property access with dot notation: user.name, address.city
        var parts = key.Split('.');
        object current = body;

        foreach (var part in parts)
        {
            if (current is Dictionary<string, object> dict && dict.TryGetValue(part, out var nextValue))
            {
                current = nextValue;
            }
            else
            {
                return $"{{{{request.body.{key}}}}}"; // Return original template if not found
            }
        }

        return ConvertToString(current);
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => "",
            string str => str,
            bool b => b.ToString().ToLowerInvariant(),
            _ => value.ToString() ?? ""
        };
    }

    public Dictionary<string, string> ProcessHeaders(Dictionary<string, string> headers, RequestContext requestContext)
    {
        var processedHeaders = new Dictionary<string, string>();
        
        foreach (var header in headers)
        {
            var processedValue = ProcessTemplate(header.Value, requestContext);
            processedHeaders[header.Key] = processedValue;
        }

        return processedHeaders;
    }
}