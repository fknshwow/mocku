using System.Text.Json;
using System.Text.RegularExpressions;
using Mocku.Web.Models;

namespace Mocku.Web.Services;

public class MockFileService
{
    private readonly string _mocksDirectory;
    private readonly ILogger<MockFileService> _logger;

    public MockFileService(IConfiguration configuration, IWebHostEnvironment environment, ILogger<MockFileService> logger)
    {
        _logger = logger;
        var configuredDirectory = configuration.GetValue<string>("MockApi:Directory") ?? "mocks";
        
        // Try to find the mocks directory in multiple locations
        var possiblePaths = new[]
        {
            Path.Combine(environment.ContentRootPath, configuredDirectory), // Project directory
            Path.Combine(Directory.GetParent(environment.ContentRootPath)?.Parent?.FullName ?? environment.ContentRootPath, configuredDirectory), // Solution root
            Path.Combine(Environment.CurrentDirectory, configuredDirectory), // Current working directory
            configuredDirectory // Relative to current directory
        };

        _mocksDirectory = possiblePaths.FirstOrDefault(Directory.Exists) ?? possiblePaths[0];
        
        // Ensure the mocks directory exists
        if (!Directory.Exists(_mocksDirectory))
        {
            Directory.CreateDirectory(_mocksDirectory);
            _logger.LogInformation("Created mocks directory: {Directory}", _mocksDirectory);
        }
    }

    public async Task<List<MockFileInfo>> GetAllMockFilesAsync()
    {
        var files = new List<MockFileInfo>();
        
        try
        {
            var jsonFiles = Directory.GetFiles(_mocksDirectory, "*.json");
            
            foreach (var filePath in jsonFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var mockDefinition = JsonSerializer.Deserialize<MockApiDefinition>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    files.Add(new MockFileInfo
                    {
                        FileName = fileName,
                        FilePath = filePath,
                        LastModified = fileInfo.LastWriteTime,
                        Size = fileInfo.Length,
                        Content = content,
                        MockDefinition = mockDefinition,
                        IsValid = mockDefinition != null && !string.IsNullOrEmpty(mockDefinition.Path)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading mock file: {FileName}", fileName);
                    files.Add(new MockFileInfo
                    {
                        FileName = fileName,
                        FilePath = filePath,
                        LastModified = fileInfo.LastWriteTime,
                        Size = fileInfo.Length,
                        Content = "",
                        MockDefinition = null,
                        IsValid = false,
                        Error = ex.Message
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mock files from directory: {Directory}", _mocksDirectory);
        }

        return files.OrderBy(f => f.FileName).ToList();
    }

    public async Task<MockFileInfo?> GetMockFileAsync(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_mocksDirectory, fileName);
            if (!File.Exists(filePath))
                return null;

            var content = await File.ReadAllTextAsync(filePath);
            var fileInfo = new FileInfo(filePath);
            
            MockApiDefinition? mockDefinition = null;
            bool isValid = false;
            string? error = null;

            try
            {
                mockDefinition = JsonSerializer.Deserialize<MockApiDefinition>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                isValid = mockDefinition != null && !string.IsNullOrEmpty(mockDefinition.Path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            return new MockFileInfo
            {
                FileName = fileName,
                FilePath = filePath,
                LastModified = fileInfo.LastWriteTime,
                Size = fileInfo.Length,
                Content = content,
                MockDefinition = mockDefinition,
                IsValid = isValid,
                Error = error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mock file: {FileName}", fileName);
            return null;
        }
    }

    public async Task<bool> SaveMockFileAsync(string fileName, string content)
    {
        try
        {
            // Ensure .json extension and convert to kebab-case
            var cleanFileName = ConvertToKebabCase(fileName.Trim());
            if (!cleanFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                cleanFileName = $"{cleanFileName}.json";
            }

            // Validate JSON before saving
            try
            {
                var mockDefinition = JsonSerializer.Deserialize<MockApiDefinition>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (mockDefinition == null || string.IsNullOrEmpty(mockDefinition.Path))
                {
                    _logger.LogWarning("Invalid mock definition in file: {FileName} - missing path", cleanFileName);
                    return false;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in file: {FileName}", cleanFileName);
                return false;
            }

            var filePath = Path.Combine(_mocksDirectory, cleanFileName);
            await File.WriteAllTextAsync(filePath, content);
            
            _logger.LogInformation("Saved mock file: {FileName}", cleanFileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving mock file: {FileName}", fileName);
            return false;
        }
    }

    private string ConvertToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        // Remove .json extension temporarily if present
        var hasJsonExtension = input.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        var nameWithoutExtension = hasJsonExtension ? 
            input.Substring(0, input.Length - 5) : input;

        // Convert to lowercase and replace various separators with dashes
        var result = nameWithoutExtension
            .ToLowerInvariant()
            .Replace(" ", "-")           // spaces to dashes
            .Replace("_", "-")           // underscores to dashes
            .Replace(".", "-")           // dots to dashes (except file extension)
            .Replace("--", "-")          // double dashes to single
            .Replace("---", "-");        // triple dashes to single

        // Remove invalid characters but keep dashes, letters, and numbers
        result = new string(result.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

        // Remove leading/trailing dashes and handle multiple consecutive dashes
        result = System.Text.RegularExpressions.Regex.Replace(result, @"-+", "-")
            .Trim('-');

        // Add back extension if it was present
        if (hasJsonExtension)
        {
            result += ".json";
        }

        return result;
    }

    public async Task<bool> CreateMockFileAsync(string fileName, string content)
    {
        return await SaveMockFileAsync(fileName, content);
    }

    public async Task<bool> UpdateMockFileAsync(string fileName, string content)
    {
        return await SaveMockFileAsync(fileName, content);
    }

    public async Task<bool> DeleteMockFileAsync(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_mocksDirectory, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted mock file: {FileName}", fileName);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting mock file: {FileName}", fileName);
            return false;
        }
    }

    public string GetMocksDirectory() => _mocksDirectory;

    public string GetDefaultMockTemplate()
    {
        return """
{
  "path": "/api/example",
  "method": "GET",
  "statusCode": 200,
  "contentType": "application/json",
  "delayMs": 0,
  "responseHeaders": {
    "X-Custom-Header": "custom-value"
  },
  "responseBody": {
    "message": "Hello World",
    "timestamp": "{{request.headers.timestamp}}",
    "data": {
      "id": "{{toNumber(request.query.id)}}",
      "active": "{{toBool(request.query.active)}}"
    }
  }
}
""";
    }
}

public class MockFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public string Content { get; set; } = string.Empty;
    public MockApiDefinition? MockDefinition { get; set; }
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}