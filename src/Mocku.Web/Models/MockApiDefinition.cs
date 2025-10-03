namespace Mocku.Web.Models;

public class MockApiDefinition
{
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public int StatusCode { get; set; } = 200;
    public object? ResponseBody { get; set; }
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();
    public int DelayMs { get; set; } = 0;
    public string? ContentType { get; set; }
    
    /// <summary>
    /// Gets whether this path contains wildcard parameters (e.g., /users/{id})
    /// </summary>
    public bool HasWildcards => Path.Contains('{') && Path.Contains('}');
    
    /// <summary>
    /// Converts the path to a regex pattern for matching
    /// Example: /users/{id} becomes ^/users/([^/]+)$
    /// </summary>
    public string GetPathPattern()
    {
        if (!HasWildcards)
            return Path;
            
        var pattern = Path;
        
        // Replace {parameter} with regex groups
        // {id} becomes ([^/]+) - matches any character except /
        // {*catch-all} becomes (.*) - matches everything including /
        pattern = System.Text.RegularExpressions.Regex.Replace(
            pattern, 
            @"\{([^}]+)\}", 
            match =>
            {
                var paramName = match.Groups[1].Value;
                // If parameter starts with *, it's a catch-all parameter
                return paramName.StartsWith("*") ? "(.*)" : "([^/]+)";
            });
            
        // Escape other regex special characters
        pattern = pattern.Replace(".", @"\.");
        
        // Anchor the pattern to match the entire path
        return $"^{pattern}$";
    }
    
    /// <summary>
    /// Extracts parameter names from the path
    /// Example: /users/{id}/posts/{postId} returns ["id", "postId"]
    /// </summary>
    public List<string> GetParameterNames()
    {
        if (!HasWildcards)
            return new List<string>();
            
        var matches = System.Text.RegularExpressions.Regex.Matches(Path, @"\{([^}]+)\}");
        return matches.Select(m => m.Groups[1].Value.TrimStart('*')).ToList();
    }
}