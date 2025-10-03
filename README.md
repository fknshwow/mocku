# Mocku - Mock API Server

Mocku is a Blazor-based mock API server that allows you to simulate downstream services during development by defining API responses in JSON files.

## Features

- **File-based Configuration**: Define mock API responses using JSON files
- **Hot Reloading**: Automatically detects changes to mock definition files
- **Management Interface**: Web-based interface to view and manage mock endpoints
- **Wildcard Parameters**: Support for dynamic path parameters like `/users/{id}`
- **Request Templating**: Use request data (body, headers, query, path) in responses
- **Parameter Substitution**: Replace path parameters in response bodies
- **Flexible Response Configuration**: Support for custom status codes, headers, content types, and response delays
- **Multiple HTTP Methods**: Support for GET, POST, PUT, DELETE, PATCH, and other HTTP methods

## Getting Started

1. **Run the application**:
   ```bash
   dotnet run
   ```

2. **Open the management interface**:
   Navigate to `http://localhost:5000` (or your configured port) to view the management interface.

3. **Create mock definitions**:
   Create JSON files in the `mocks` directory (created automatically on first run).

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
    "X-User-Agent": "{{request.headers.user-agent}}"
  },
  "responseBody": {
    "id": "{{request.path.id}}",
    "name": "{{request.body.name}}",
    "email": "{{request.body.email}}",
    "message": "Created user {{request.body.name}} with ID {{request.path.id}}"
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

## Request Templating

### Template Variables

Use request data in your responses with these template variables:

| Template | Description | Example |
|----------|-------------|---------|
| `{{request.path.paramName}}` | Path parameters | `{{request.path.id}}` |
| `{{request.headers.headerName}}` | Request headers | `{{request.headers.authorization}}` |
| `{{request.query.paramName}}` | Query parameters | `{{request.query.page}}` |
| `{{request.body.fieldName}}` | Request body fields | `{{request.body.name}}` |
| `{{request.body.nested.field}}` | Nested body fields | `{{request.body.user.email}}` |

### Type Conversion Functions

Use explicit type conversion functions for precise control over output types:

| Function | Description | Example | Output Type |
|----------|-------------|---------|-------------|
| `toBool()` | Convert to boolean | `{{toBool(request.query.active)}}` | `true`/`false` |
| `toNumber()` | Convert to integer | `{{toNumber(request.path.id)}}` | `123` |
| `toInt()` | Convert to integer (alias) | `{{toInt(request.body.age)}}` | `25` |
| `toFloat()` | Convert to decimal | `{{toFloat(request.body.price)}}` | `99.99` |
| `toString()` | Convert to string | `{{toString(request.body.id)}}` | `"123"` |
| `toArray()` | Convert to array | `{{toArray(request.body.tags)}}` | `["tag1", "tag2"]` |
| `toObject()` | Convert to object | `{{toObject(request.body.data)}}` | `{"key": "value"}` |

### Boolean Conversion Rules

The `toBool()` function recognizes these values as `true`:
- `"true"`, `"1"`, `"yes"`, `"on"` (case-insensitive)
- Any non-zero number

These values are recognized as `false`:
- `"false"`, `"0"`, `"no"`, `"off"` (case-insensitive)
- Zero (`0`, `0.0`)

### Typed Value Replacement

Template variables in JSON responses are automatically converted to appropriate types:

- **Numbers**: `"id": "{{request.body.id}}"` becomes `"id": 123`
- **Booleans**: `"active": "{{request.body.active}}"` becomes `"active": true` 
- **Strings**: `"name": "{{request.body.name}}"` becomes `"name": "John"`
- **Objects/Arrays**: Preserved as-is from request body

### Examples

#### Explicit Type Conversion
```json
{
  "path": "/api/users/{id}",
  "method": "GET",
  "responseBody": {
    "id": "{{toNumber(request.path.id)}}",         // ? 123 (number)
    "active": "{{toBool(request.query.active)}}",  // ? true (boolean)
    "name": "{{toString(request.path.id)}}",       // ? "123" (string)
    "score": "{{toFloat(request.query.score)}}"    // ? 95.5 (decimal)
  }
}
```

#### Request Body with Type Functions
```json
{
  "path": "/api/users",
  "method": "POST",
  "responseBody": {
    "id": "{{toNumber(request.body.id)}}",
    "name": "{{toString(request.body.name)}}",
    "age": "{{toInt(request.body.age)}}",
    "active": "{{toBool(request.body.active)}}",
    "roles": "{{toArray(request.body.roles)}}",
    "profile": "{{toObject(request.body.profile)}}"
  }
}
```

#### Query Parameters with Types
```json
{
  "path": "/api/products",
  "method": "GET", 
  "responseBody": {
    "page": "{{toNumber(request.query.page)}}",       // "1" ? 1
    "inStock": "{{toBool(request.query.inStock)}}",   // "true" ? true
    "price": "{{toFloat(request.query.price)}}",      // "99.99" ? 99.99
    "search": "{{toString(request.query.q)}}"         // Explicit string
  }
}
```

#### Special Array Conversion
```json
{
  "path": "/api/tags",
  "method": "GET",
  "responseBody": {
    "userTags": "{{toArray(request.body.tags)}}",     // From request body
    "staticTags": "{{toArray(red,green,blue)}}",      // CSV string ? array
    "categories": "{{toArray(request.query.cats)}}"   // Query param ? array
  }
}
```

## Wildcard Parameters

### Path Wildcards

Use `{paramName}` in the path to create dynamic routes:

- `/users/{id}` - Matches `/users/123`, `/users/abc`, etc.
- `/users/{id}/posts/{postId}` - Matches `/users/123/posts/456`
- `/files/{*path}` - Catch-all: matches `/files/docs/readme.txt`

### Legacy Parameter Substitution

For backward compatibility, path parameters can still use:

- `"{paramName}"` - Replaces with parameter value in JSON strings
- `{{paramName}}` - Replaces with parameter value anywhere in response

## Example Mock Definitions

### Simple GET Request
```json
{
  "path": "/api/health",
  "method": "GET",
  "statusCode": 200,
  "responseBody": {
    "status": "healthy"
  }
}
```

### POST with Request Body Echo
```json
{
  "path": "/api/users",
  "method": "POST",
  "statusCode": 201,
  "responseHeaders": {
    "Location": "/api/users/{{request.body.id}}"
  },
  "responseBody": {
    "id": "{{request.body.id}}",
    "name": "{{request.body.name}}",
    "email": "{{request.body.email}}",
    "message": "Created user {{request.body.name}}"
  }
}
```

### Query Parameter Search
```json
{
  "path": "/api/search",
  "method": "GET",
  "responseBody": {
    "query": "{{request.query.q}}",
    "page": "{{request.query.page}}",
    "results": [
      {
        "title": "Search result for: {{request.query.q}}"
      }
    ]
  }
}
```

### Multi-Parameter Wildcard
```json
{
  "path": "/api/users/{userId}/posts/{postId}",
  "method": "GET",
  "statusCode": 200,
  "responseBody": {
    "id": "{{request.path.postId}}",
    "title": "Post {{request.path.postId}} by User {{request.path.userId}}",
    "authorId": "{{request.path.userId}}"
  }
}
```

### Comprehensive Echo Endpoint
```json
{
  "path": "/api/echo",
  "method": "POST",
  "responseHeaders": {
    "X-User-Agent": "{{request.headers.user-agent}}",
    "X-Host": "{{request.headers.host}}"
  },
  "responseBody": {
    "headers": {
      "userAgent": "{{request.headers.user-agent}}",
      "host": "{{request.headers.host}}"
    },
    "query": {
      "format": "{{request.query.format}}"
    },
    "body": {
      "message": "{{request.body.message}}",
      "user": "{{request.body.user.name}}"
    }
  }
}
```

## Configuration

You can configure the mocks directory path in `appsettings.json`:

```json
{
  "MockApi": {
    "Directory": "mocks"
  }
}
```

## How It Works

1. **File Watching**: The application monitors the configured mocks directory for JSON files
2. **Hot Reloading**: When files are added, modified, or deleted, the mock definitions are automatically updated
3. **Request Capture**: Incoming requests have their path, headers, query, and body captured
4. **Request Matching**: HTTP requests are matched against mock definitions by method and path
5. **Wildcard Processing**: Supports both exact path matching and regex-based wildcard matching
6. **Template Processing**: Request data is used to populate template variables in responses and headers
7. **Response Generation**: Matching requests receive the configured mock response with template substitution
8. **Management Interface**: The root path (`/`) serves a web interface showing all active mock definitions

## Reserved Paths

- `/` - Management interface (reserved)
- `/_*` - Internal Blazor paths (reserved)
- `/css/*` - Static CSS files (reserved)
- `/js/*` - Static JavaScript files (reserved)

All other paths can be used for mock API endpoints.

## Example Usage

### Testing User Creation with Request Body
```bash
curl -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -H "X-API-Key: test-key" \
  -d '{"id": "123", "name": "John Doe", "email": "john@example.com"}'
```

### Testing Search with Query Parameters
```bash
curl "http://localhost:5000/api/search?q=test&page=1&category=books"
```

### Testing User Update with Path and Body
```bash
curl -X PUT http://localhost:5000/api/users/123 \
  -H "Content-Type: application/json" \
  -d '{"name": "John Updated", "email": "john.updated@example.com"}'
```

### Testing Echo Endpoint
```bash
curl -X POST http://localhost:5000/api/echo \
  -H "Content-Type: application/json" \
  -H "X-User-ID: user123" \
  -d '{"message": "Hello World", "user": {"name": "John"}}'
```

## Development Tips

1. **File Names**: File names don't affect functionality - use descriptive names
2. **Template Testing**: Use the echo endpoint to test template variables
3. **Request Headers**: All header names are converted to lowercase for templating
4. **Nested Body Fields**: Use dot notation for nested JSON fields: `{{request.body.user.name}}`
5. **Error Handling**: Invalid templates are left as-is in the response
6. **Debugging**: Check application logs for template processing information
7. **Hot Reloading**: Changes to JSON files are picked up automatically
8. **Path Priority**: Exact path matches take precedence over wildcard matches