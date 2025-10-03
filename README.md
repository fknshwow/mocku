# Mocku - Mock API Server

Mocku is a comprehensive Blazor-based mock API server that allows you to simulate downstream services during development by defining API responses in JSON files. It features an intelligent management interface, advanced templating, and smart sample request generation.

## ?? Key Features

### Core Mock API Features
- **File-based Configuration**: Define mock API responses using JSON files
- **Hot Reloading**: Automatically detects changes to mock definition files
- **Advanced Request Templating**: Use request data (body, headers, query, path) in responses with type conversion
- **Wildcard Parameters**: Support for dynamic path parameters like `/users/{id}` and catch-all routes
- **Multiple HTTP Methods**: Support for GET, POST, PUT, DELETE, PATCH, and other HTTP methods
- **Custom Response Configuration**: Status codes, headers, content types, and response delays

### Management Interface
- **Web-based Dashboard**: Comprehensive interface at `http://localhost:5000`
- **Active Endpoints View**: See all loaded mock endpoints with search and sorting
- **File Management**: Create, edit, and delete mock files directly in the interface
- **Request Logging**: Real-time monitoring of all incoming requests
- **Sample Request Generation**: Intelligent generation of cURL, JavaScript, C#, and raw HTTP requests

### Advanced Template Processing
- **Type Conversion Functions**: `toBool()`, `toNumber()`, `toFloat()`, etc.
- **Nested Object Support**: Deep property access with dot notation
- **JSON Type Preservation**: Automatic conversion to proper JSON types
- **Function-based Templates**: Smart conversion based on field types
- **Header and Query Templating**: Use request data in response headers

### Developer Experience
- **Smart Sample Generation**: Analyzes response templates to generate realistic request samples
- **Live Reload Interface**: Management interface updates automatically
- **Request/Response Logging**: Track all API interactions in real-time
- **File Search and Filtering**: Quickly find and manage mock definitions
- **Interactive Documentation**: Built-in examples and guidance

## Getting Started

1. **Run the application**:
   ```bash
   dotnet run
   ```

2. **Open the management interface**:
   Navigate to `http://localhost:5000` to access the comprehensive management dashboard.

3. **Create mock definitions**:
   - Use the web interface "New Mock File" button, or
   - Create JSON files manually in the `mocks` directory

## Mock Definition Format

Create JSON files in the `mocks` directory with the following structure:

```json
{
  "path": "/api/users/{id}",
  "method": "POST",
  "statusCode": 201,
  "contentType": "application/json",
  "delayMs": 100,
  "responseHeaders": {
    "Location": "/api/users/{{request.body.id}}",
    "X-User-Agent": "{{request.headers.user-agent}}",
    "X-Request-ID": "{{request.headers.x-request-id}}"
  },
  "responseBody": {
    "id": "{{toNumber(request.body.id)}}",
    "name": "{{request.body.name}}",
    "email": "{{request.body.email}}",
    "active": "{{toBool(request.body.active)}}",
    "profile": "{{toObject(request.body.profile)}}",
    "message": "Created user {{request.body.name}} with ID {{toNumber(request.body.id)}}"
  }
}
```

### Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `path` | string | ? | - | The API endpoint path (supports wildcards) |
| `method` | string | ? | "GET" | HTTP method (GET, POST, PUT, DELETE, etc.) |
| `statusCode` | number | ? | 200 | HTTP status code |
| `contentType` | string | ? | "application/json" | Response content type |
| `delayMs` | number | ? | 0 | Artificial delay in milliseconds |
| `responseHeaders` | object | ? | {} | Custom response headers (supports templating) |
| `responseBody` | any | ? | null | Response content (supports templating) |

## ?? Advanced Request Templating

### Template Variables

Use request data in your responses with these template variables:

| Template | Description | Example |
|----------|-------------|---------|
| `{{request.path.paramName}}` | Path parameters | `{{request.path.id}}` |
| `{{request.headers.headerName}}` | Request headers | `{{request.headers.authorization}}` |
| `{{request.query.paramName}}` | Query parameters | `{{request.query.page}}` |
| `{{request.body.fieldName}}` | Request body fields | `{{request.body.name}}` |
| `{{request.body.nested.field}}` | Nested body fields (dot notation) | `{{request.body.user.preferences.theme}}` |

### ?? Type Conversion Functions

Use explicit type conversion functions for precise control over output types:

| Function | Description | Example | Input | Output |
|----------|-------------|---------|-------|--------|
| `toBool()` | Convert to boolean | `{{toBool(request.body.active)}}` | `"true"`, `"1"`, `"yes"` | `true` |
| `toNumber()` | Convert to integer | `{{toNumber(request.path.id)}}` | `"123"` | `123` |
| `toInt()` | Convert to integer (alias) | `{{toInt(request.body.age)}}` | `"25"` | `25` |
| `toFloat()` | Convert to decimal | `{{toFloat(request.body.price)}}` | `"99.99"` | `99.99` |
| `toString()` | Convert to string | `{{toString(request.body.id)}}` | `123` | `"123"` |
| `toArray()` | Convert to array | `{{toArray(request.body.tags)}}` | `"tag1,tag2"` | `["tag1", "tag2"]` |
| `toObject()` | Convert to object | `{{toObject(request.body.data)}}` | JSON string | Object |

### Boolean Conversion Rules

The `toBool()` function recognizes these values as `true`:
- **Strings**: `"true"`, `"1"`, `"yes"`, `"on"` (case-insensitive)
- **Numbers**: Any non-zero number
- **Booleans**: `true`

These values are recognized as `false`:
- **Strings**: `"false"`, `"0"`, `"no"`, `"off"` (case-insensitive)
- **Numbers**: Zero (`0`, `0.0`)
- **Booleans**: `false`

### ?? Automatic Type Detection

Template variables in JSON responses are automatically converted to appropriate types:

```json
{
  "id": "{{request.body.id}}",           // "123" ? 123 (number)
  "name": "{{request.body.name}}",       // "John" ? "John" (string)
  "active": "{{request.body.active}}",   // "true" ? true (boolean)
  "price": "{{request.body.price}}",     // "99.99" ? 99.99 (number)
  "tags": "{{request.body.tags}}"        // ["a","b"] ? ["a","b"] (array)
}
```

## ?? Sample Request Generation

Mocku intelligently analyzes your mock response templates and generates realistic sample requests in multiple formats:

### Supported Formats
- **cURL Commands**: Ready-to-run terminal commands
- **JavaScript Fetch**: Modern browser/Node.js code
- **C# HttpClient**: .NET application code
- **Raw HTTP**: Pure HTTP format for any tool

### Smart Generation Features
- **Template Analysis**: Examines response templates to determine required request fields
- **Nested Object Support**: Generates complex request structures like `{ "user": { "preferences": { "theme": "dark" } } }`
- **Type-aware Values**: Generates appropriate sample data based on field names and types
- **Header Intelligence**: Includes required headers referenced in response templates
- **Query Parameter Extraction**: Adds necessary query parameters to URLs

### Example: Intelligent Generation

For a response template using `{{toBool(request.body.user.preferences.notifications)}}`, the sample generator creates:

```json
{
  "user": {
    "preferences": {
      "notifications": true
    }
  }
}
```

## ?? Management Interface Features

### Active Endpoints Tab
- **Endpoint Count Badge**: Shows total number of loaded endpoints
- **Search & Filter**: Find endpoints by path, method, status code, or content type
- **Sortable Columns**: Click headers to sort by any column
- **Sample Generation**: Click "Sample" button for any endpoint to generate requests
- **Direct Testing**: "Test" button for non-wildcard GET endpoints

### Mock Files Tab
- **File Count Badge**: Shows total number of mock files
- **File Status**: Valid/Invalid indicators with error details
- **Create/Edit/Delete**: Full file management capabilities
- **Search**: Find files by name, endpoint, or method
- **File Size & Timestamps**: Detailed file information

### Request Logs Tab
- **Real-time Monitoring**: Live request/response logging
- **Request Count Badge**: Shows number of logged requests
- **Mock vs Passthrough**: Visual indicators for matched vs unmatched requests
- **Detailed View**: Expandable request/response details
- **Search & Filter**: Find specific requests by various criteria
- **Performance Metrics**: Response times and request details

### Documentation Tab
- **Interactive Examples**: Copy-paste ready examples
- **Template Reference**: Complete guide to all template features
- **Type Conversion Guide**: Detailed function documentation
- **Best Practices**: Tips for effective mock development

## ?? Example Mock Definitions

### User Management with Smart Types
```json
{
  "path": "/api/users",
  "method": "POST",
  "statusCode": 201,
  "responseHeaders": {
    "Location": "/api/users/{{toNumber(request.body.id)}}",
    "X-User-Created": "{{request.headers.x-request-id}}"
  },
  "responseBody": {
    "id": "{{toNumber(request.body.id)}}",
    "name": "{{toString(request.body.name)}}",
    "email": "{{toString(request.body.email)}}",
    "age": "{{toInt(request.body.age)}}",
    "active": "{{toBool(request.body.active)}}",
    "preferences": {
      "theme": "{{toString(request.body.preferences.theme)}}",
      "notifications": "{{toBool(request.body.preferences.notifications)}}"
    },
    "metadata": {
      "created": "{{now}}",
      "requestId": "{{request.headers.x-request-id}}",
      "userAgent": "{{request.headers.user-agent}}"
    }
  }
}
```

### Search with Query Parameters
```json
{
  "path": "/api/search",
  "method": "GET",
  "responseBody": {
    "query": "{{toString(request.query.q)}}",
    "page": "{{toNumber(request.query.page)}}",
    "limit": "{{toNumber(request.query.limit)}}",
    "includeInactive": "{{toBool(request.query.includeInactive)}}",
    "sortBy": "{{toString(request.query.sort)}}",
    "results": [
      {
        "id": 1,
        "title": "Search result for: {{request.query.q}}",
        "relevance": "{{toFloat(request.query.relevance)}}"
      }
    ],
    "meta": {
      "totalResults": 42,
      "currentPage": "{{toNumber(request.query.page)}}",
      "hasMore": "{{toBool(request.query.hasMore)}}"
    }
  }
}
```

### Product Catalog with Complex Nesting
```json
{
  "path": "/api/products/{productId}",
  "method": "PUT",
  "statusCode": 200,
  "responseBody": {
    "id": "{{toNumber(request.path.productId)}}",
    "name": "{{toString(request.body.name)}}",
    "description": "{{toString(request.body.description)}}",
    "price": "{{toFloat(request.body.price)}}",
    "inStock": "{{toBool(request.body.inStock)}}",
    "categories": "{{toArray(request.body.categories)}}",
    "specifications": "{{toObject(request.body.specifications)}}",
    "metadata": {
      "lastModified": "{{now}}",
      "modifiedBy": "{{request.headers.x-user-id}}",
      "version": "{{toNumber(request.body.version)}}"
    }
  }
}
```

### Echo Endpoint for Testing
```json
{
  "path": "/api/echo",
  "method": "POST",
  "responseHeaders": {
    "X-Echo-Request-ID": "{{request.headers.x-request-id}}",
    "X-Echo-User-Agent": "{{request.headers.user-agent}}"
  },
  "responseBody": {
    "echo": {
      "headers": {
        "authorization": "{{request.headers.authorization}}",
        "contentType": "{{request.headers.content-type}}",
        "userAgent": "{{request.headers.user-agent}}"
      },
      "query": {
        "format": "{{toString(request.query.format)}}",
        "debug": "{{toBool(request.query.debug)}}"
      },
      "body": "{{toObject(request.body)}}",
      "path": {
        "fullPath": "{{request.path.*}}"
      }
    },
    "processed": {
      "timestamp": "{{now}}",
      "bodyAsString": "{{toString(request.body)}}",
      "hasAuthHeader": "{{toBool(request.headers.authorization)}}"
    }
  }
}
```

## ??? Wildcard Parameters

### Standard Wildcards
- `/users/{id}` - Matches `/users/123`, `/users/abc`
- `/users/{id}/posts/{postId}` - Matches `/users/123/posts/456`
- `/products/{category}/{id}` - Matches `/products/electronics/123`

### Catch-all Wildcards
- `/files/{*path}` - Matches `/files/docs/readme.txt`, `/files/images/logo.png`
- `/api/v1/{*endpoint}` - Matches any path under `/api/v1/`

## ?? Configuration

Configure the application in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "MockApi": {
    "Directory": "mocks"
  }
}
```

## ??? How It Works

### Request Processing Pipeline
1. **File Watching**: Monitors the mocks directory for changes
2. **Hot Reloading**: Automatically updates mock definitions when files change
3. **Request Capture**: Captures all request data (path, headers, query, body)
4. **Pattern Matching**: Matches requests against mock definitions by method and path
5. **Template Processing**: Processes templates with type conversion and nested object support
6. **Response Generation**: Returns mock response with template substitution
7. **Request Logging**: Logs all requests for monitoring and debugging

### Template Processing Engine
- **JSON-aware Processing**: Preserves JSON structure and types
- **Function Resolution**: Processes type conversion functions
- **Nested Object Traversal**: Handles deep property access with dot notation
- **Type Coercion**: Automatically converts string templates to appropriate JSON types
- **Error Handling**: Gracefully handles missing values and invalid templates

## ?? Reserved Paths

- `/` - Management interface dashboard
- `/_*` - Internal Blazor SignalR and component paths
- `/css/*` - Static CSS files
- `/js/*` - Static JavaScript files

All other paths are available for mock API endpoints.

## ?? Development Tips

### Best Practices
1. **Descriptive File Names**: Use clear, descriptive names for mock files
2. **Template Testing**: Use echo endpoints to verify template variables
3. **Type Consistency**: Use type conversion functions for consistent API responses
4. **Error Simulation**: Create mocks for error scenarios (404, 500, etc.)
5. **Request Validation**: Use templates to echo back request data for validation

### Debugging & Troubleshooting
1. **Request Logs**: Check the Request Logs tab for detailed request/response information
2. **Template Errors**: Invalid templates are left as-is in responses for debugging
3. **File Validation**: Check the Mock Files tab for validation errors
4. **Live Reload**: File changes are reflected immediately - no restart needed
5. **Sample Generation**: Use the sample request generator to test your endpoints

### Advanced Usage
1. **Nested Data**: Use dot notation for complex object structures
2. **Header Templating**: Include request headers in response headers
3. **Conditional Logic**: Combine templates with type functions for smart responses
4. **Performance Testing**: Use `delayMs` to simulate network latency
5. **Integration Testing**: Use comprehensive echo endpoints for request validation

## ?? Example Usage

### Testing with Generated Samples

1. **Navigate to Management Interface**: `http://localhost:5000`
2. **Find Your Endpoint**: Use the search feature in Active Endpoints
3. **Click "Sample"**: Generates ready-to-use request code
4. **Choose Format**: cURL, JavaScript, C#, or Raw HTTP
5. **Copy & Test**: One-click copy to clipboard

### Example Generated cURL:
```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -H "User-Agent: MockuSampleRequest/1.0" \
  -H "X-Client-App: sample-x-client-app-value" \
  -H "X-Request-ID: 31f7a7de-431f-4e10-a78a-b06aec423959" \
  -d '{
    "user": {
      "id": 123,
      "name": "John Doe",
      "email": "john.doe@example.com",
      "role": "Developer",
      "preferences": {
        "theme": "dark",
        "notifications": true
      }
    }
  }' \
  "http://localhost:5000/api/users"
```

### Manual Testing Examples

#### User Creation with Complex Data
```bash
curl -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -H "X-Request-ID: req-123" \
  -d '{
    "id": "123",
    "name": "John Doe", 
    "email": "john@example.com",
    "age": "30",
    "active": "true",
    "preferences": {
      "theme": "dark",
      "notifications": "true"
    }
  }'
```

#### Search with Multiple Parameters
```bash
curl "http://localhost:5000/api/search?q=javascript&page=1&limit=10&includeInactive=false&sort=relevance"
```

#### Product Update with Nested Data
```bash
curl -X PUT http://localhost:5000/api/products/456 \
  -H "Content-Type: application/json" \
  -H "X-User-ID: user123" \
  -d '{
    "name": "Updated Product",
    "price": "29.99",
    "inStock": "true",
    "categories": ["electronics", "gadgets"],
    "specifications": {
      "weight": "1.2kg",
      "dimensions": "10x5x2cm"
    }
  }'
```

---

## ?? Getting Started Quickly

1. **Clone and Run**:
   ```bash
   git clone <repo-url>
   cd mocku
   dotnet run
   ```

2. **Open Management Interface**: Navigate to `http://localhost:5000`

3. **Create Your First Mock**: Click "New Mock File" and use the interactive editor

4. **Test with Samples**: Use the "Sample" button to generate test requests

5. **Monitor Requests**: Check the Request Logs tab to see your API in action

Mocku provides everything you need for comprehensive API mocking with an intuitive interface and powerful templating system! ??