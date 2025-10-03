using Mocku.Web.Models;
using Mocku.Web.Services;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Mocku.Web.Middleware;

public class MockApiMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MockApiService _mockApiService;
    private readonly TemplateProcessor _templateProcessor;
    private readonly RequestLogService _requestLogService;
    private readonly ILogger<MockApiMiddleware> _logger;

    public MockApiMiddleware(
        RequestDelegate next, 
        MockApiService mockApiService, 
        TemplateProcessor templateProcessor,
        RequestLogService requestLogService,
        ILogger<MockApiMiddleware> logger)
    {
        _next = next;
        _mockApiService = mockApiService;
        _templateProcessor = templateProcessor;
        _requestLogService = requestLogService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var logEntry = new RequestLogEntry
        {
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? "",
            QueryString = context.Request.QueryString.Value,
            Headers = context.Request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value.ToArray())),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            ClientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };

        // Read request body if present
        if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            logEntry.RequestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        // Try to find a matching mock
        var mock = _mockApiService.GetMockDefinition(context.Request.Method, context.Request.Path.Value ?? "");
        
        if (mock != null)
        {
            logEntry.IsMatchedByMock = true;
            logEntry.MockFileName = GetMockFileName(mock);

            try
            {
                // Extract path parameters
                var pathParameters = _mockApiService.ExtractPathParameters(mock, context.Request.Path.Value ?? "");
                
                // Create request context for template processing
                var requestContext = RequestContext.FromHttpContext(context, pathParameters, logEntry.RequestBody ?? "");

                // Apply delay if specified
                if (mock.DelayMs > 0)
                {
                    await Task.Delay(mock.DelayMs);
                }

                // Set status code
                context.Response.StatusCode = mock.StatusCode;
                logEntry.StatusCode = mock.StatusCode;

                // Set content type
                if (!string.IsNullOrEmpty(mock.ContentType))
                {
                    context.Response.ContentType = mock.ContentType;
                }

                // Set response headers
                foreach (var header in mock.ResponseHeaders)
                {
                    var processedValue = _templateProcessor.ProcessTemplate(header.Value, requestContext);
                    context.Response.Headers[header.Key] = processedValue;
                    logEntry.ResponseHeaders[header.Key] = processedValue;
                }

                // Process response body with templating
                string mockResponseBody;
                if (mock.ResponseBody is JsonElement jsonElement)
                {
                    var jsonString = JsonSerializer.Serialize(jsonElement, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    mockResponseBody = _templateProcessor.ProcessTemplate(jsonString, requestContext);
                }
                else
                {
                    mockResponseBody = _templateProcessor.ProcessTemplate(mock.ResponseBody?.ToString() ?? "", requestContext);
                }

                logEntry.ResponseBody = mockResponseBody;
                await context.Response.WriteAsync(mockResponseBody);

                stopwatch.Stop();
                logEntry.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                
                _requestLogService.LogRequest(logEntry);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing mock response for {Method} {Path}", 
                    context.Request.Method, context.Request.Path);
                
                context.Response.StatusCode = 500;
                logEntry.StatusCode = 500;
                logEntry.ResponseBody = $"{{\"error\": \"Internal server error processing mock: {ex.Message}\"}}";
                
                stopwatch.Stop();
                logEntry.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                _requestLogService.LogRequest(logEntry);
                
                await context.Response.WriteAsync(logEntry.ResponseBody);
                return;
            }
        }

        // No mock found, continue to next middleware
        logEntry.IsMatchedByMock = false;
        
        // Capture response details from the pipeline
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
            
            logEntry.StatusCode = context.Response.StatusCode;
            logEntry.ResponseHeaders = context.Response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value.ToArray()));
            
            responseBody.Seek(0, SeekOrigin.Begin);
            logEntry.ResponseBody = await new StreamReader(responseBody).ReadToEndAsync();
            
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
            stopwatch.Stop();
            logEntry.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
            _requestLogService.LogRequest(logEntry);
        }
    }

    private string? GetMockFileName(MockApiDefinition mock)
    {
        // This is a simplified version. In a more advanced implementation,
        // you could track the source file for each mock definition
        return $"{mock.Method.ToLower()}-{mock.Path.Replace("/", "-").Replace("{", "").Replace("}", "").Trim('-')}.json";
    }
}