using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mocku.Web.Models;

namespace Mocku.Web.Services;

public class MockApiService : IDisposable
{
    private readonly ConcurrentDictionary<string, MockApiDefinition> _mockDefinitions = new();
    private readonly FileSystemWatcher _fileWatcher;
    private readonly string _mocksDirectory;
    private readonly ILogger<MockApiService> _logger;

    // Event to notify when mock definitions change
    public event Action? MockDefinitionsChanged;

    public MockApiService(IConfiguration configuration, ILogger<MockApiService> logger, IWebHostEnvironment environment)
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
        
        _logger.LogInformation("Mock API service initializing with directory: {Directory}", _mocksDirectory);
        
        // Log all attempted paths for debugging
        _logger.LogDebug("Attempted paths: {Paths}", string.Join(", ", possiblePaths));
        
        // Ensure the mocks directory exists
        if (!Directory.Exists(_mocksDirectory))
        {
            Directory.CreateDirectory(_mocksDirectory);
            _logger.LogInformation("Created mocks directory: {Directory}", _mocksDirectory);
        }

        // Initialize file watcher
        _fileWatcher = new FileSystemWatcher(_mocksDirectory, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _fileWatcher.Created += OnFileChanged;
        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Deleted += OnFileDeleted;
        _fileWatcher.Renamed += OnFileRenamed;

        // Load existing files
        LoadAllMockFiles();
        
        _logger.LogInformation("Mock API service initialized with {Count} definitions", _mockDefinitions.Count);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (Path.GetExtension(e.FullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                // Add a small delay to ensure file write is complete
                await Task.Delay(100);
                LoadMockFile(e.FullPath);
                // Notify subscribers of changes
                MockDefinitionsChanged?.Invoke();
            });
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (Path.GetExtension(e.FullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(e.Name);
            _mockDefinitions.TryRemove(fileName, out _);
            _logger.LogInformation("Removed mock definition: {FileName}", fileName);
            // Notify subscribers of changes
            MockDefinitionsChanged?.Invoke();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        bool changed = false;
        
        if (Path.GetExtension(e.OldFullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var oldFileName = Path.GetFileNameWithoutExtension(e.OldName);
            _mockDefinitions.TryRemove(oldFileName, out _);
            changed = true;
        }

        if (Path.GetExtension(e.FullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            LoadMockFile(e.FullPath);
            changed = true;
        }

        if (changed)
        {
            // Notify subscribers of changes
            MockDefinitionsChanged?.Invoke();
        }
    }

    private void LoadAllMockFiles()
    {
        try
        {
            _logger.LogInformation("Loading mock files from directory: {Directory}", _mocksDirectory);
            
            if (!Directory.Exists(_mocksDirectory))
            {
                _logger.LogWarning("Mocks directory does not exist: {Directory}", _mocksDirectory);
                return;
            }

            var jsonFiles = Directory.GetFiles(_mocksDirectory, "*.json");
            _logger.LogInformation("Found {Count} JSON files in mocks directory", jsonFiles.Length);
            
            foreach (var file in jsonFiles)
            {
                LoadMockFile(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mock files from directory: {Directory}", _mocksDirectory);
        }
    }

    private void LoadMockFile(string filePath)
    {
        try
        {
            _logger.LogDebug("Loading mock file: {FilePath}", filePath);
            
            var content = File.ReadAllText(filePath);
            var mockDefinition = JsonSerializer.Deserialize<MockApiDefinition>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mockDefinition != null && !string.IsNullOrEmpty(mockDefinition.Path))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                _mockDefinitions.AddOrUpdate(fileName, mockDefinition, (key, oldValue) => mockDefinition);
                
                var pathType = mockDefinition.HasWildcards ? "wildcard" : "exact";
                _logger.LogInformation("Loaded {PathType} mock definition from {FileName}: {Method} {Path}", 
                    pathType, fileName, mockDefinition.Method, mockDefinition.Path);
            }
            else
            {
                _logger.LogWarning("Invalid mock definition in file: {FilePath} - missing path or null definition", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mock file: {FilePath}", filePath);
        }
    }

    public MockApiDefinition? GetMockDefinition(string method, string path)
    {
        // First try exact path match for better performance
        var exactMatch = _mockDefinitions.Values
            .FirstOrDefault(m => 
                !m.HasWildcards &&
                string.Equals(m.Method, method, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
                
        if (exactMatch != null)
        {
            _logger.LogDebug("Found exact match for {Method} {Path}", method, path);
            return exactMatch;
        }

        // Then try wildcard pattern matching
        var wildcardMatch = _mockDefinitions.Values
            .Where(m => 
                m.HasWildcards &&
                string.Equals(m.Method, method, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(m => 
            {
                try
                {
                    var pattern = m.GetPathPattern();
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    return regex.IsMatch(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error matching wildcard pattern for {Path}", m.Path);
                    return false;
                }
            });

        if (wildcardMatch != null)
        {
            _logger.LogDebug("Found wildcard match for {Method} {Path} using pattern {Pattern}", 
                method, path, wildcardMatch.Path);
        }

        return wildcardMatch;
    }

    public Dictionary<string, string> ExtractPathParameters(MockApiDefinition mockDefinition, string requestPath)
    {
        var parameters = new Dictionary<string, string>();
        
        if (!mockDefinition.HasWildcards)
            return parameters;

        try
        {
            var pattern = mockDefinition.GetPathPattern();
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var match = regex.Match(requestPath);
            
            if (match.Success)
            {
                var paramNames = mockDefinition.GetParameterNames();
                for (int i = 0; i < paramNames.Count && i < match.Groups.Count - 1; i++)
                {
                    parameters[paramNames[i]] = match.Groups[i + 1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting path parameters from {Path}", requestPath);
        }

        return parameters;
    }

    public IEnumerable<MockApiDefinition> GetAllMockDefinitions()
    {
        return _mockDefinitions.Values.ToList();
    }

    public string GetMocksDirectory() => _mocksDirectory;

    public void Dispose()
    {
        _fileWatcher?.Dispose();
    }
}