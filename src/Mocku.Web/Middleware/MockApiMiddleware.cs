using System.Text;
using System.Text.Json;
using Mocku.Web.Models;
using Mocku.Web.Services;

namespace Mocku.Web.Middleware;

public class MockApiMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MockApiService _mockApiService;
    private readonly TemplateProcessor _templateProcessor;
    private readonly ILogger<MockApiMiddleware> _logger;

    public MockApiMiddleware(RequestDelegate next, MockApiService mockApiService, TemplateProcessor templateProcessor, ILogger<MockApiMiddleware> logger)
    {
        _next = next;
        _mockApiService = mockApiService;
        _templateProcessor = templateProcessor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        // Skip root path and static assets
        if (path == "/" || path.StartsWith("/_") || path.StartsWith("/css") || path.StartsWith("/js"))
        {
            await _next(context);
            return;
        }

        // Try to find a matching mock definition
        var mockDefinition = _mockApiService.GetMockDefinition(method, path);
        if (mockDefinition != null)
        {
            _logger.LogInformation("Serving mock response for {Method} {Path} using definition {MockPath}", 
                method, path, mockDefinition.Path);

            // Extract path parameters if this is a wildcard route
            var pathParameters = _mockApiService.ExtractPathParameters(mockDefinition, path);
            if (pathParameters.Any())
            {
                _logger.LogDebug("Extracted path parameters: {Parameters}", 
                    string.Join(", ", pathParameters.Select(p => $"{p.Key}={p.Value}")));
            }

            // Read request body for template processing
            string requestBody = "";
            if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
            {
                context.Request.EnableBuffering(); // Allow reading the body multiple times
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Reset position for potential downstream middleware
            }

            // Create request context for template processing
            var requestContext = RequestContext.FromHttpContext(context, pathParameters, requestBody);

            // Log request context for debugging
            _logger.LogDebug("Request context - Path params: {PathParams}, Headers: {HeaderCount}, Query: {QueryCount}, Body size: {BodySize}",
                string.Join(", ", requestContext.Path.Select(p => $"{p.Key}={p.Value}")),
                requestContext.Headers.Count,
                requestContext.Query.Count,
                requestBody.Length);

            // Add delay if specified
            if (mockDefinition.DelayMs > 0)
            {
                await Task.Delay(mockDefinition.DelayMs);
            }

            // Set status code
            context.Response.StatusCode = mockDefinition.StatusCode;

            // Process and set response headers with template support
            var processedHeaders = _templateProcessor.ProcessHeaders(mockDefinition.ResponseHeaders, requestContext);
            foreach (var header in processedHeaders)
            {
                context.Response.Headers[header.Key] = header.Value;
            }

            // Set content type
            var contentType = mockDefinition.ContentType ?? "application/json";
            context.Response.ContentType = contentType;

            // Write response body with template processing
            if (mockDefinition.ResponseBody != null)
            {
                string responseContent;
                
                if (mockDefinition.ResponseBody is string stringResponse)
                {
                    responseContent = _templateProcessor.ProcessTemplate(stringResponse, requestContext);
                }
                else
                {
                    var jsonResponse = JsonSerializer.Serialize(mockDefinition.ResponseBody, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    });
                    responseContent = _templateProcessor.ProcessJsonTemplate(jsonResponse, requestContext);
                }

                await context.Response.WriteAsync(responseContent, Encoding.UTF8);
            }

            return;
        }

        // Continue to next middleware if no mock found
        await _next(context);
    }
}