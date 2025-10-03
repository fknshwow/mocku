using Mocku.Web.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mocku.Web.Services;

public class RequestGeneratorService
{
    public SampleRequest GenerateSampleRequest(MockApiDefinition mock, string baseUrl = "http://localhost:5000")
    {
        var sampleRequest = new SampleRequest
        {
            Method = mock.Method,
            Url = GenerateUrl(mock, baseUrl),
            Headers = GenerateHeaders(mock),
            Body = GenerateBody(mock)
        };

        return sampleRequest;
    }

    public string GenerateCurlCommand(MockApiDefinition mock, string baseUrl = "http://localhost:5000")
    {
        var url = GenerateUrl(mock, baseUrl);
        var command = $"curl -X {mock.Method}";
        
        // Add headers
        var headers = GenerateHeaders(mock);
        foreach (var header in headers)
        {
            command += $" \\\n  -H \"{header.Key}: {header.Value}\"";
        }

        // Add body for POST/PUT/PATCH requests
        if (ShouldIncludeBody(mock.Method))
        {
            var body = GenerateBody(mock);
            if (!string.IsNullOrEmpty(body))
            {
                command += $" \\\n  -d '{body}'";
            }
        }

        command += $" \\\n  \"{url}\"";
        return command;
    }

    public string GenerateJavaScriptFetch(MockApiDefinition mock, string baseUrl = "http://localhost:5000")
    {
        var url = GenerateUrl(mock, baseUrl);
        var headers = GenerateHeaders(mock);
        
        var js = $"fetch('{url}', {{\n";
        js += $"  method: '{mock.Method}',\n";
        
        if (headers.Any())
        {
            js += "  headers: {\n";
            js += string.Join(",\n", headers.Select(h => $"    '{h.Key}': '{h.Value}'"));
            js += "\n  }";
        }

        if (ShouldIncludeBody(mock.Method))
        {
            var body = GenerateBody(mock);
            if (!string.IsNullOrEmpty(body))
            {
                if (headers.Any()) js += ",";
                js += $"\n  body: JSON.stringify({body})";
            }
        }

        js += "\n})\n.then(response => response.json())\n.then(data => console.log(data));";
        return js;
    }

    public string GenerateCSharpHttpClient(MockApiDefinition mock, string baseUrl = "http://localhost:5000")
    {
        var url = GenerateUrl(mock, baseUrl);
        var headers = GenerateHeaders(mock);
        
        var cs = "using var client = new HttpClient();\n";
        
        // Add headers
        foreach (var header in headers.Where(h => !h.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase)))
        {
            cs += $"client.DefaultRequestHeaders.Add(\"{header.Key}\", \"{header.Value}\");\n";
        }

        if (mock.Method.ToUpper() == "GET")
        {
            cs += $"var response = await client.GetAsync(\"{url}\");\n";
        }
        else if (ShouldIncludeBody(mock.Method))
        {
            var body = GenerateBody(mock);
            var contentType = headers.FirstOrDefault(h => h.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase)).Value ?? "application/json";
            
            cs += $"var content = new StringContent(\"{EscapeString(body)}\", Encoding.UTF8, \"{contentType}\");\n";
            cs += $"var response = await client.{GetHttpClientMethod(mock.Method)}(\"{url}\", content);\n";
        }
        else
        {
            cs += $"var response = await client.{GetHttpClientMethod(mock.Method)}(\"{url}\");\n";
        }

        cs += "var result = await response.Content.ReadAsStringAsync();";
        return cs;
    }

    private string GenerateUrl(MockApiDefinition mock, string baseUrl)
    {
        var path = mock.Path;
        
        // Replace path parameters with sample values
        if (mock.HasWildcards)
        {
            var paramNames = mock.GetParameterNames();
            foreach (var paramName in paramNames)
            {
                var sampleValue = GenerateSampleValue(paramName);
                if (paramName.StartsWith("*"))
                {
                    // Catch-all parameter
                    path = path.Replace($"{{{paramName}}}", sampleValue);
                }
                else
                {
                    path = path.Replace($"{{{paramName}}}", sampleValue);
                }
            }
        }

        // Analyze response for required query parameters
        var queryParams = AnalyzeResponseForQueryParameters(mock);
        
        // Add sample query parameters for GET requests or if templating requires them
        if (mock.Method.ToUpper() == "GET" || queryParams.Any())
        {
            var queryString = "";
            
            if (queryParams.Any())
            {
                // Add query parameters that are referenced in the response
                var paramPairs = queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value.ToString())}");
                queryString = string.Join("&", paramPairs);
            }
            
            // Add default pagination for GET requests if no specific query params found
            if (mock.Method.ToUpper() == "GET" && !queryParams.Any() && !path.Contains("?"))
            {
                queryString = "page=1&limit=10";
            }
            
            if (!string.IsNullOrEmpty(queryString))
            {
                var separator = path.Contains("?") ? "&" : "?";
                path += separator + queryString;
            }
        }

        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private Dictionary<string, object> AnalyzeResponseForQueryParameters(MockApiDefinition mock)
    {
        var queryParams = new Dictionary<string, object>();
        
        try
        {
            // Convert response body to JSON string for analysis
            string responseJson = "";
            if (mock.ResponseBody != null)
            {
                if (mock.ResponseBody is string stringResponse)
                {
                    responseJson = stringResponse;
                }
                else
                {
                    responseJson = JsonSerializer.Serialize(mock.ResponseBody, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            // Also check response headers for templating
            var allTemplateStrings = new List<string> { responseJson };
            allTemplateStrings.AddRange(mock.ResponseHeaders.Values);

            foreach (var templateString in allTemplateStrings)
            {
                if (string.IsNullOrEmpty(templateString)) continue;

                // Find request.query references
                var queryMatches = System.Text.RegularExpressions.Regex.Matches(
                    templateString,
                    @"\{\{request\.query\.([^}]+)\}\}",
                    RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in queryMatches)
                {
                    var fieldName = match.Groups[1].Value;
                    var sampleValue = GenerateSampleValueForField(fieldName);
                    queryParams[fieldName] = sampleValue;
                }
            }
        }
        catch (Exception)
        {
            // If analysis fails, return empty dictionary
        }

        return queryParams;
    }

    private Dictionary<string, string> GenerateHeaders(MockApiDefinition mock)
    {
        var headers = new Dictionary<string, string>();

        if (ShouldIncludeBody(mock.Method))
        {
            headers["Content-Type"] = mock.ContentType ?? "application/json";
        }

        headers["Accept"] = "application/json";
        headers["User-Agent"] = "MockuSampleRequest/1.0";

        // Analyze response for required headers
        var requiredHeaders = AnalyzeResponseForRequiredHeaders(mock);
        foreach (var header in requiredHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private Dictionary<string, string> AnalyzeResponseForRequiredHeaders(MockApiDefinition mock)
    {
        var requiredHeaders = new Dictionary<string, string>();
        
        try
        {
            // Convert response body to JSON string for analysis
            string responseJson = "";
            if (mock.ResponseBody != null)
            {
                if (mock.ResponseBody is string stringResponse)
                {
                    responseJson = stringResponse;
                }
                else
                {
                    responseJson = JsonSerializer.Serialize(mock.ResponseBody, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            // Also check response headers for templating
            var allTemplateStrings = new List<string> { responseJson };
            allTemplateStrings.AddRange(mock.ResponseHeaders.Values);

            foreach (var templateString in allTemplateStrings)
            {
                if (string.IsNullOrEmpty(templateString)) continue;

                // Find request.headers references
                var headerMatches = System.Text.RegularExpressions.Regex.Matches(
                    templateString,
                    @"\{\{request\.headers\.([^}]+)\}\}",
                    RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in headerMatches)
                {
                    var headerName = match.Groups[1].Value;
                    var sampleValue = GenerateHeaderSampleValue(headerName);
                    requiredHeaders[headerName] = sampleValue;
                }
            }
        }
        catch (Exception)
        {
            // If analysis fails, return empty dictionary
        }

        return requiredHeaders;
    }

    private string GenerateHeaderSampleValue(string headerName)
    {
        return headerName.ToLower() switch
        {
            "authorization" => "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
            "x-api-key" => "sample-api-key-12345",
            "x-user-id" => "123",
            "x-session-id" => "sess_abc123xyz",
            "x-request-id" => Guid.NewGuid().ToString(),
            "x-correlation-id" => Guid.NewGuid().ToString(),
            "user-agent" => "MockuSampleRequest/1.0",
            "content-type" => "application/json",
            "accept" => "application/json",
            "x-client-version" => "1.0.0",
            "x-platform" => "web",
            _ => $"sample-{headerName}-value"
        };
    }

    private string GenerateBody(MockApiDefinition mock)
    {
        if (!ShouldIncludeBody(mock.Method))
            return "";

        // Try to analyze the response body to determine what request body is expected
        var requiredFields = AnalyzeResponseForRequiredFields(mock);
        
        if (requiredFields.Any())
        {
            // Generate a request body that satisfies the response templating
            return GenerateRequestBodyFromAnalysis(requiredFields);
        }

        // Fallback to path-based generation if no templating analysis is available
        return GeneratePathBasedBody(mock);
    }

    private Dictionary<string, object> AnalyzeResponseForRequiredFields(MockApiDefinition mock)
    {
        var requiredFields = new Dictionary<string, object>();
        
        try
        {
            // Convert response body to JSON string for analysis
            string responseJson = "";
            if (mock.ResponseBody != null)
            {
                if (mock.ResponseBody is string stringResponse)
                {
                    responseJson = stringResponse;
                }
                else
                {
                    responseJson = JsonSerializer.Serialize(mock.ResponseBody, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            // Also check response headers for templating
            var allTemplateStrings = new List<string> { responseJson };
            allTemplateStrings.AddRange(mock.ResponseHeaders.Values);

            foreach (var templateString in allTemplateStrings)
            {
                if (string.IsNullOrEmpty(templateString)) continue;

                // Find ALL template patterns that reference request.body
                // This pattern will catch: {{request.body.xxx}}, {{toXxx(request.body.yyy)}}, etc.
                var allBodyTemplates = System.Text.RegularExpressions.Regex.Matches(
                    templateString,
                    @"\{\{[^}]*request\.body\.([^})\s]+)[^}]*\}\}",
                    RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in allBodyTemplates)
                {
                    var fieldPath = match.Groups[1].Value.Trim();
                    // Clean up any function syntax remnants
                    fieldPath = fieldPath.TrimEnd(')', ' ', '\t', '\n', '\r');
                    
                    var sampleValue = GenerateSampleValueForField(fieldPath);
                    SetNestedProperty(requiredFields, fieldPath, sampleValue);
                }

                // Additional specific patterns for common cases
                var specificPatterns = new[]
                {
                    @"\{\{request\.body\.([^}]+)\}\}",  // Simple: {{request.body.field}}
                    @"\{\{toBool\(request\.body\.([^)]+)\)\}\}",  // toBool function
                    @"\{\{toNumber\(request\.body\.([^)]+)\)\}\}",  // toNumber function
                    @"\{\{toString\(request\.body\.([^)]+)\)\}\}",  // toString function
                    @"\{\{toInt\(request\.body\.([^)]+)\)\}\}",  // toInt function
                    @"\{\{toFloat\(request\.body\.([^)]+)\)\}\}",  // toFloat function
                    @"\{\{toArray\(request\.body\.([^)]+)\)\}\}",  // toArray function
                    @"\{\{toObject\(request\.body\.([^)]+)\)\}\}"  // toObject function
                };

                foreach (var pattern in specificPatterns)
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(templateString, pattern, RegexOptions.IgnoreCase);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var fieldPath = match.Groups[1].Value.Trim();
                        var sampleValue = GenerateSampleValueForField(fieldPath);
                        SetNestedProperty(requiredFields, fieldPath, sampleValue);
                    }
                }
            }
        }
        catch (Exception)
        {
            // If analysis fails, return empty dictionary to fall back to path-based generation
        }

        return requiredFields;
    }

    private void SetNestedProperty(Dictionary<string, object> target, string path, object value)
    {
        var parts = path.Split('.');
        var current = target;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (!current.ContainsKey(part))
            {
                current[part] = new Dictionary<string, object>();
            }
            
            if (current[part] is Dictionary<string, object> dict)
            {
                current = dict;
            }
            else
            {
                // If there's a conflict, overwrite with a new dictionary
                current[part] = new Dictionary<string, object>();
                current = (Dictionary<string, object>)current[part];
            }
        }

        current[parts[parts.Length - 1]] = value;
    }

    private object GenerateSampleValueForField(string fieldPath)
    {
        var fieldName = fieldPath.Split('.').Last().ToLower();
        
        return fieldName switch
        {
            "id" or "userid" or "productid" or "orderid" => 123,
            "name" or "username" or "firstname" or "lastname" => "John Doe",
            "email" or "emailaddress" => "john.doe@example.com",
            "phone" or "phonenumber" => "+1-555-0123",
            "age" => 30,
            "price" or "amount" or "cost" => 29.99,
            "quantity" or "count" => 5,
            "active" or "enabled" or "isactive" or "notifications" or "subscribe" or "opted" => true,
            "disabled" or "inactive" or "blocked" or "hidden" => false,
            "description" or "desc" => "Sample description",
            "title" => "Sample Title",
            "category" => "electronics",
            "status" => "active",
            "type" => "standard",
            "theme" => "dark",
            "mode" => "advanced",
            "level" => "premium",
            "url" or "website" => "https://example.com",
            "address" => "123 Main St, City, State 12345",
            "city" => "New York",
            "state" => "NY",
            "zipcode" or "zip" => "12345",
            "country" => "USA",
            "company" or "organization" => "Acme Corp",
            "role" or "position" => "Developer",
            "date" or "createdate" or "updatedate" => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            "score" or "rating" => 4.5,
            "tags" => new string[] { "tag1", "tag2" },
            _ => DetermineValueByFieldName(fieldName)
        };
    }

    private object DetermineValueByFieldName(string fieldName)
    {
        // Additional heuristics based on common patterns
        if (fieldName.EndsWith("id")) return 123;
        if (fieldName.EndsWith("name")) return "Sample Name";
        if (fieldName.EndsWith("email")) return "sample@example.com";
        if (fieldName.EndsWith("phone")) return "+1-555-0123";
        if (fieldName.EndsWith("date")) return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (fieldName.EndsWith("url")) return "https://example.com";
        
        // Boolean patterns
        if (fieldName.StartsWith("is") || fieldName.StartsWith("has") || fieldName.StartsWith("can") || fieldName.StartsWith("should")) return true;
        if (fieldName.EndsWith("enabled") || fieldName.EndsWith("active") || fieldName.EndsWith("visible") || 
            fieldName.EndsWith("notifications") || fieldName.EndsWith("alerts") || fieldName.EndsWith("subscribe")) return true;
        if (fieldName.EndsWith("disabled") || fieldName.EndsWith("inactive") || fieldName.EndsWith("hidden")) return false;
        
        // Numeric patterns
        if (fieldName.Contains("price") || fieldName.Contains("amount") || fieldName.Contains("cost")) return 19.99;
        if (fieldName.Contains("count") || fieldName.Contains("quantity") || fieldName.Contains("number")) return 1;
        if (fieldName.Contains("score") || fieldName.Contains("rating")) return 4.5;
        if (fieldName.Contains("percentage") || fieldName.Contains("percent")) return 85.5;
        
        // String patterns
        if (fieldName.Contains("theme")) return "dark";
        if (fieldName.Contains("mode")) return "automatic";
        if (fieldName.Contains("language") || fieldName.Contains("locale")) return "en-US";
        if (fieldName.Contains("timezone")) return "UTC";
        if (fieldName.Contains("currency")) return "USD";
        
        return "sample value";
    }

    private string GenerateRequestBodyFromAnalysis(Dictionary<string, object> requiredFields)
    {
        return JsonSerializer.Serialize(requiredFields, new JsonSerializerOptions { WriteIndented = true });
    }

    private string GeneratePathBasedBody(MockApiDefinition mock)
    {
        // Original path-based generation logic
        var path = mock.Path.ToLower();
        
        if (path.Contains("user"))
        {
            return JsonSerializer.Serialize(new
            {
                id = 123,
                name = "John Doe",
                email = "john.doe@example.com",
                age = 30,
                active = true
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        else if (path.Contains("product"))
        {
            return JsonSerializer.Serialize(new
            {
                id = 456,
                name = "Sample Product",
                description = "This is a sample product",
                price = 29.99,
                category = "electronics",
                inStock = true
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        else if (path.Contains("order"))
        {
            return JsonSerializer.Serialize(new
            {
                id = 789,
                items = new[]
                {
                    new { productId = 456, quantity = 2, price = 29.99 }
                },
                total = 59.98,
                customerEmail = "customer@example.com"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            // Generic sample body
            return JsonSerializer.Serialize(new
            {
                id = 1,
                name = "Sample Item",
                description = "This is a sample request body",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                active = true
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private string GenerateSampleValue(string paramName)
    {
        var cleanName = paramName.TrimStart('*').ToLower();
        
        return cleanName switch
        {
            "id" or "userid" or "productid" or "orderid" => "123",
            "name" or "username" => "john-doe",
            "email" => "user@example.com",
            "category" => "electronics",
            "status" => "active",
            "type" => "sample",
            "version" => "v1",
            _ => "sample-value"
        };
    }

    private bool ShouldIncludeBody(string method)
    {
        return method.ToUpper() is "POST" or "PUT" or "PATCH";
    }

    private string GetHttpClientMethod(string method)
    {
        return method.ToUpper() switch
        {
            "POST" => "PostAsync",
            "PUT" => "PutAsync",
            "PATCH" => "PatchAsync",
            "DELETE" => "DeleteAsync",
            _ => "GetAsync"
        };
    }

    private string EscapeString(string input)
    {
        return input?.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}

public class SampleRequest
{
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = "";
}