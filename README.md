# Freetool

A free, open-source alternative to Retool for building internal tools and dashboards. Freetool helps companies create CRUD interfaces around their internal APIs with authentication, authorization, and audit logging - all without requiring developers to build custom admin interfaces.

# Contributing

## 🏗️ Architecture

This project follows **Onion Architecture** principles to maintain clean separation of concerns and enable comprehensive testing:

```
┌─────────────────────────────────────────┐
│              API Layer                  │  ← Controllers, Middleware, Models
├─────────────────────────────────────────┤
│         Infrastructure Layer            │  ← Repositories, External Services
├─────────────────────────────────────────┤
│         Application Layer               │  ← Use Cases, DTOs, Interfaces
├─────────────────────────────────────────┤
│            Domain Layer                 │  ← Entities, Value Objects, Events
└─────────────────────────────────────────┘
```

### Core Principles

- **Dependency Inversion**: All dependencies point inward toward the domain
- **Pure Business Logic**: Domain and Application layers contain no infrastructure concerns
- **Testability**: Business logic is easily unit tested without external dependencies
- **Flexibility**: Infrastructure can be swapped without affecting core functionality
- **Functional Design**: Uses F# discriminated unions and pattern matching for command handling instead of object-oriented use cases
- **Event Store Integration**: Guarantees 1:1 consistency between business operations and audit trail using transactional event sourcing

## 📊 OpenTelemetry & Distributed Tracing

Freetool includes comprehensive **OpenTelemetry (OTEL)** instrumentation with automatic business logic tracing across all layers. The system provides complete observability from HTTP requests down to database operations without requiring manual span creation.

### 🔍 Tracing Coverage

The application instruments three layers automatically:

1. **HTTP Layer**: ASP.NET Core instrumentation captures all incoming requests, response times, and HTTP status codes
2. **Business Logic Layer**: Custom AutoTracing system automatically generates spans for all command operations with detailed attributes
3. **Database Layer**: Entity Framework Core instrumentation tracks all SQL queries, connection times, and database operations

### ⚡ AutoTracing System

The AutoTracing system uses **pure reflection** and **naming conventions** to automatically generate OTEL spans and attributes for all business operations without requiring manual configuration.

#### Key Features

- **Zero Configuration**: Works automatically for all controllers - just register one line in DI
- **Naming Convention Based**: Converts `CreateUser` command to `"user.create"` span name
- **Automatic Attributes**: Extracts all command parameters and result data as OTEL attributes
- **Security First**: Automatically skips sensitive fields (password, token, secret, key, credential)
- **Architecture Compliant**: Uses pure reflection to maintain clean onion architecture separation

#### How It Works

```fsharp
// 1. Command Analysis - Automatic span name generation
CreateUser → "user.create"
GetUserById → "user.get_user_by_id"
UpdateUserEmail → "user.update_user_email"
DeleteUser → "user.delete"

// 2. Attribute Extraction - Automatic parameter capture
CreateUser(ValidatedUser { Name = "John"; Email = "john@example.com" })
→ Attributes: user.name = "John", user.email = "john@example.com"

// 3. Result Tracking - Automatic response capture
UserResult(UserDto { Id = "123"; Name = "John" })
→ Attributes: result.id = "123", result.name = "John"
```

#### Adding Tracing to New Controllers

Adding OTEL tracing to a new controller requires just **one line** in dependency injection:

```fsharp
// In Program.fs
builder.Services.AddScoped<IGenericCommandHandler<INewRepository, NewCommand, NewCommandResult>>
    (fun serviceProvider ->
        let newHandler = serviceProvider.GetRequiredService<NewHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "new_entity" newHandler activitySource)
```

### 📊 Current Tracing Coverage

| Layer          | Automatic Spans     | What Gets Traced                       |
|----------------|---------------------|----------------------------------------|
| HTTP           | ✅ ASP.NET Core     | Request/response, status codes, routes |
| Business Logic | ✅ AutoTracing      | Commands, DTOs, domain operations      |
| Database       | ✅ Entity Framework | SQL queries, connection times          |
| Repository     | ❌ Not yet          | Individual repository method calls     |
| Domain Models  | ❌ Not yet          | Domain entity operations               |

### 🔧 Configuration

#### Basic Setup

```fsharp
// Configure OTEL in Program.fs
builder.Services
    .AddOpenTelemetry()
    .WithTracing(fun tracing ->
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("freetool-api", "1.0.0"))
            .AddSource("Freetool.Api")                    // Custom business logic spans
            .AddAspNetCoreInstrumentation()               // HTTP request/response spans
            .AddEntityFrameworkCoreInstrumentation()      // Database query spans
            .AddOtlpExporter(fun options ->
                options.Endpoint <- System.Uri("http://localhost:4317") // Jaeger/OTEL collector
                options.Protocol <- OtlpExportProtocol.Grpc))
```

#### Environment Variables

```bash
# OTEL Collector/Jaeger endpoint
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Service identification
OTEL_SERVICE_NAME=freetool-api
OTEL_SERVICE_VERSION=1.0.0
```

### 📈 Observability Examples

#### Sample Trace Hierarchy
```
🌐 HTTP GET /user/123                                   [200ms]
├── 🔧 user.get_user_by_id                              [180ms]
│   ├── 🗄️ SELECT * FROM Users WHERE Id = @p0           [50ms]
│   └── 🔄 Domain Mapping & Validation                  [5ms]
└── 🌐 HTTP Response Serialization                      [15ms]
```

#### Automatic Span Attributes
```
Span: user.create
├── operation.type = "create"
├── user.name = "John Doe"
├── user.email = "john@example.com"
├── user.profile_pic_url = "https://example.com/pic.jpg"
├── result.id = "user-123"
├── result.created_at = "2024-01-15T10:30:00Z"
└── span.status = "ok"
```

### 🔐 Security & Privacy

The AutoTracing system automatically protects sensitive data:

- **Field Filtering**: Automatically skips any field containing: `password`, `token`, `secret`, `key`, `credential`
- **Configurable**: Additional sensitive patterns can be added to the `shouldSkipField` function
- **Secure by Default**: Unknown field types are safely converted to strings with null checks

## 🗃️ Event Store Integration & Audit Trail

Freetool implements a **transactional event sourcing pattern** that guarantees perfect **1:1 consistency** between business operations and audit trail. This ensures that every database change is atomically recorded as domain events, providing complete auditability and compliance.

### 🎯 Key Benefits

- **Atomic Consistency**: Events and business data are saved in the same database transaction
- **Complete Audit Trail**: Every business operation automatically generates audit events
- **Architecture Compliance**: Maintains clean onion architecture with domain events in the core
- **Type Safety**: F# discriminated unions ensure correct event structure at compile time
- **Performance**: Events stored in same database - no distributed transaction overhead

### 🔧 How It Works

#### 1. Domain Events Collection
Domain aggregates collect uncommitted events as business operations are performed:

```fsharp
// Domain Layer - User.fs
type User = {
    State: UserData                    // Business data
    UncommittedEvents: IDomainEvent list  // Events to be persisted
}

let updateName (newName: string) (user: User) : Result<User, DomainError> =
    // Business logic validation
    if String.IsNullOrWhiteSpace newName then
        Error(ValidationError "User name cannot be empty")
    else
        // Update business data
        let updatedData = { user.State with Name = newName.Trim() }

        // Collect domain event
        let nameChangedEvent = UserEvents.userUpdated user.State.Id [NameChanged(oldName, newName)]

        // Return updated aggregate with new event
        Ok {
            State = updatedData
            UncommittedEvents = user.UncommittedEvents @ [nameChangedEvent]
        }
```

#### 2. Transactional Persistence
The Infrastructure layer saves both business data and events atomically:

```fsharp
// Infrastructure Layer - UserRepository.fs
member _.UpdateAsync(user: ValidatedUser) : Task<Result<unit, DomainError>> = task {
    use transaction = context.Database.BeginTransaction()

    try
        // 1. Save business data to Users table
        let! _ = context.SaveChangesAsync()

        // 2. Save events to Events table (SAME transaction)
        let events = User.getUncommittedEvents user
        for event in events do
            do! eventRepository.SaveEventAsync event

        // 3. Commit everything atomically
        transaction.Commit()
        return Ok()
    with
    | ex ->
        transaction.Rollback()
        return Error(InvalidOperation "Transaction failed")
}
```

#### 3. Application Layer Orchestration
Command handlers use domain methods that automatically generate events:

```fsharp
// Application Layer - UserHandler.fs
| UpdateUserName(userId, dto) ->
    let! userOption = userRepository.GetByIdAsync userIdObj
    match userOption with
    | Some user ->
        // Domain method automatically creates events
        match User.updateName dto.Name user with
        | Ok updatedUser ->
            // Repository saves both data and events atomically
            match! userRepository.UpdateAsync updatedUser with
            | Ok() -> return Ok(UserResult(mapUserToDto updatedUser))
```

### 4. Event Store Schema

Events are stored in a dedicated table with full metadata:

```sql
CREATE TABLE Events (
    Id TEXT NOT NULL PRIMARY KEY,
    EventId TEXT NOT NULL,           -- Domain event ID
    EventType TEXT NOT NULL,         -- UserCreatedEvent, UserUpdatedEvent, etc.
    EntityType TEXT NOT NULL,        -- User, Tool, etc.
    EntityId TEXT NOT NULL,          -- ID of the affected entity
    EventData TEXT NOT NULL,         -- JSON serialized event data
    OccurredAt TEXT NOT NULL,        -- When the business operation happened
    CreatedAt TEXT NOT NULL          -- When the event was persisted
);
```

### 🔍 Event Types

The system generates comprehensive events for all business operations:

```fsharp
// Domain Events - UserEvents.fs
type UserCreatedEvent = {
    UserId: UserId
    Name: string
    Email: Email
    ProfilePicUrl: Url option
    OccurredAt: DateTime
    EventId: Guid
}

type UserUpdatedEvent = {
    UserId: UserId
    Changes: UserChange list          // What specifically changed
    OccurredAt: DateTime
    EventId: Guid
}

type UserChange =
    | NameChanged of oldValue: string * newValue: string
    | EmailChanged of oldValue: Email * newValue: Email
    | ProfilePicChanged of oldValue: Url option * newValue: Url option
```

### ⚡ Operational Flow

1. **User Action**: HTTP request to update user name
2. **Domain Logic**: `User.updateName` validates and creates `UserUpdatedEvent`
3. **Atomic Save**: Repository saves user data + event in single transaction
4. **Audit Trail**: Event is immediately queryable via `/audit` endpoints
5. **Tracing**: OpenTelemetry captures the entire operation flow

### 🧪 Testing Benefits

Domain events enable comprehensive testing without infrastructure dependencies:

```fsharp
[<Fact>]
let ``User name update should generate correct event`` () =
    // Arrange
    let user = User.create "John Doe" email None

    // Act
    let result = User.updateName "Jane Doe" user

    // Assert
    match result with
    | Ok updatedUser ->
        let events = User.getUncommittedEvents updatedUser
        Assert.Single(events)

        match events.[0] with
        | :? UserUpdatedEvent as event ->
            Assert.Equal("John Doe", event.Changes.[0].OldValue)
            Assert.Equal("Jane Doe", event.Changes.[0].NewValue)
```

This event store integration pattern ensures that Freetool maintains perfect audit compliance while preserving clean architecture principles and providing comprehensive observability.

## 🔐 Authorization with OpenFGA

Freetool implements **fine-grained authorization** using [OpenFGA](https://openfga.dev/), a high-performance authorization system based on Google's Zanzibar. This enables flexible, relationship-based access control across all resources while maintaining clean onion architecture separation.

### 🎯 Authorization Model

The authorization system enforces a hierarchical permission model:

**Entities:**
- **Users** - Individual users in the system
- **Organization** - Global scope with organization-wide admins
- **Spaces** - Top-level containers for organizing resources, apps, and folders

**Permissions (10 total):**
1. `create_resource` - Create new API resources
2. `edit_resource` - Modify existing resources
3. `delete_resource` - Remove resources
4. `create_app` - Create new applications
5. `edit_app` - Modify existing applications
6. `delete_app` - Remove applications
7. `run_app` - Execute applications
8. `create_folder` - Create new folders
9. `edit_folder` - Modify existing folders
10. `delete_folder` - Remove folders

**Authorization Rules:**
- **Global Admins** → All 10 permissions on ALL spaces
- **Space Moderators** → All 10 permissions on their space + manage space membership
- **Space Members** → Specific permissions granted by space moderator
- **Global Admins** → Can assign space moderators

### 🏗️ Architecture Implementation

Following onion architecture principles, OpenFGA is integrated across all layers:

#### Domain Layer
Authorization concepts are expressed as pure domain concerns (future):
- Permission value objects
- Resource ownership rules
- Access control domain events

#### Application Layer (`Freetool.Application/Interfaces/IAuthorizationService.fs`)
Defines contracts for authorization operations:

```fsharp
type IAuthorizationService =
    // Store management
    abstract member CreateStoreAsync: CreateStoreRequest -> Task<StoreResponse>
    abstract member WriteAuthorizationModelAsync: unit -> Task<AuthorizationModelResponse>

    // Relationship management
    abstract member CreateRelationshipsAsync: RelationshipTuple list -> Task<unit>
    abstract member UpdateRelationshipsAsync: UpdateRelationshipsRequest -> Task<unit>
    abstract member DeleteRelationshipsAsync: RelationshipTuple list -> Task<unit>

    // Permission checking
    abstract member CheckPermissionAsync: user:string -> relation:string -> object:string -> Task<bool>
```

#### Infrastructure Layer (`Freetool.Infrastructure/Services/OpenFgaService.fs`)
Implements OpenFGA client integration:
- Manages OpenFGA SDK client lifecycle
- Translates domain concepts to OpenFGA tuples
- Handles store and model operations
- Executes permission checks

#### API Layer (`Freetool.Api/Program.fs`)
Wires up authorization service in dependency injection:

```fsharp
builder.Services.AddScoped<IAuthorizationService>(fun serviceProvider ->
    let apiUrl = builder.Configuration["OpenFGA:ApiUrl"]
    let storeId = builder.Configuration["OpenFGA:StoreId"]

    if System.String.IsNullOrEmpty(storeId) then
        OpenFgaService(apiUrl)
    else
        OpenFgaService(apiUrl, storeId)
)
```

### 📝 OpenFGA Model Definition

The authorization model uses OpenFGA DSL syntax:

```
model
  schema 1.1

type user

type organization
  relations
    define admin: [user]

type space
  relations
    define member: [user]
    define moderator: [user, organization#admin]
    define create_resource: [user, organization#admin] or moderator
    define edit_resource: [user, organization#admin] or moderator
    define delete_resource: [user, organization#admin] or moderator
    define create_app: [user, organization#admin] or moderator
    define edit_app: [user, organization#admin] or moderator
    define delete_app: [user, organization#admin] or moderator
    define run_app: [user, organization#admin] or moderator
    define create_folder: [user, organization#admin] or moderator
    define edit_folder: [user, organization#admin] or moderator
    define delete_folder: [user, organization#admin] or moderator
```

### 🔧 Managing Relationships

**Creating Relationships:**
```fsharp
// Make Alice a member of the engineering space
authService.CreateRelationshipsAsync([
    { User = "user:alice"
      Relation = "member"
      Object = "space:engineering" }
])

// Grant Bob create_resource permission on main space
authService.CreateRelationshipsAsync([
    { User = "user:bob"
      Relation = "create_resource"
      Object = "space:main" }
])
```

**Updating Relationships (Atomic):**
```fsharp
// Promote Carol from member to moderator
authService.UpdateRelationshipsAsync({
    TuplesToAdd = [{ User = "user:carol"; Relation = "moderator"; Object = "space:engineering" }]
    TuplesToRemove = [{ User = "user:carol"; Relation = "member"; Object = "space:engineering" }]
})
```

**Deleting Relationships:**
```fsharp
// Revoke Dave's run_app permission
authService.DeleteRelationshipsAsync([
    { User = "user:dave"
      Relation = "run_app"
      Object = "space:main" }
])
```

### 🔍 Permission Checks

```fsharp
// Check if Alice can create resources in main space
let! canCreate = authService.CheckPermissionAsync("user:alice", "create_resource", "space:main")

// Check if Bob is a space moderator
let! isModerator = authService.CheckPermissionAsync("user:bob", "moderator", "space:engineering")
```

### ⚙️ Configuration

**Environment Variables:**
```bash
# OpenFGA server endpoint
OPENFGA_API_URL=http://openfga:8090

# Store ID (obtained after creating a store)
OPENFGA_STORE_ID=01HVMMBCMGZNT3SED4Z17ECXCA
```

**appsettings.Development.json:**
```json
{
  "OpenFGA": {
    "ApiUrl": "http://openfga:8090",
    "StoreId": ""
  }
}
```

### 🐳 Docker Setup

OpenFGA runs as a containerized service in `docker-compose.yml`:

```yaml
openfga-migrate:
  image: openfga/openfga:latest
  command: migrate
  environment:
    - OPENFGA_DATASTORE_ENGINE=sqlite
    - OPENFGA_DATASTORE_URI=file:/home/nonroot/openfga.db

openfga:
  image: openfga/openfga:latest
  command: run
  ports:
    - "8090:8090"  # HTTP API
    - "8091:8091"  # gRPC API
    - "3030:3030"  # Playground UI
  depends_on:
    openfga-migrate:
      condition: service_completed_successfully
```

### 🎯 Benefits

- **Scalable**: Handles millions of authorization checks per second
- **Flexible**: Relationship-based model adapts to complex permission requirements
- **Auditable**: All permission changes tracked as relationship tuples
- **Testable**: Authorization logic isolated from business logic
- **Architecture Compliant**: Clean separation via onion architecture layers
- **Type Safe**: F# types ensure correct permission structures at compile time

## 📁 Project Structure

```
Freetool.sln
├── src/
│   ├── Freetool.Domain/              # 🎯 Pure business logic (innermost)
│   │   ├── src/
│   │   │   ├── Types.fs              # Core domain types and common definitions
│   │   │   ├── Entities/             # Domain entities (aggregates)
│   │   │   │   ├── User.fs           # User aggregate with business rules
│   │   │   │   ├── Tool.fs           # Tool/endpoint configuration
│   │   │   │   ├── Dashboard.fs      # Dashboard layout and components
│   │   │   │   └── AuditLog.fs       # Audit trail entity
│   │   │   ├── ValueObjects/         # Immutable value objects
│   │   │   │   ├── UserId.fs         # Strongly-typed user identifier
│   │   │   │   ├── ToolId.fs         # Strongly-typed tool identifier
│   │   │   │   ├── Email.fs          # Email with validation rules
│   │   │   │   └── Permissions.fs    # Permission and role definitions
│   │   │   ├── Services/             # Domain services (pure functions)
│   │   │   │   ├── ToolValidation.fs # Tool configuration validation logic
│   │   │   │   ├── PermissionService.fs # Permission calculation and checking
│   │   │   │   └── AuditService.fs   # Audit event creation logic
│   │   │   └── Events/               # Domain events for integration
│   │   │       ├── UserEvents.fs     # User-related domain events
│   │   │       ├── ToolEvents.fs     # Tool-related domain events
│   │   │       └── AuditEvents.fs    # Audit-related domain events
│   │   └── test/                     # 🧪 Pure unit tests (fast)
│   │
│   ├── Freetool.Application/         # 🔧 Application orchestration
│   │   ├── src/
│   │   │   ├── DTOs/                 # Data transfer objects for boundaries
│   │   │   │   └── UserDtos.fs       # User-related DTOs
│   │   │   ├── Interfaces/           # Repository and service contracts
│   │   │   │   ├── IUserRepository.fs # User data access interface
│   │   │   │   ├── IToolRepository.fs # Tool data access interface
│   │   │   │   ├── IDashboardRepository.fs
│   │   │   │   ├── IAuditRepository.fs
│   │   │   │   └── IEmailService.fs  # Email service interface
│   │   │   ├── Commands/             # Command definitions using discriminated unions
│   │   │   │   └── UserCommands.fs   # User commands and result types
│   │   │   ├── Handlers/             # Command handlers using pattern matching
│   │   │   │   └── UserHandler.fs    # User command handler module
│   │   │   └── Common/               # Shared application utilities
│   │   │       ├── Result.fs         # Result type for error handling
│   │   │       ├── Validation.fs     # Cross-cutting validation logic
│   │   │       └── Mapping.fs        # Domain ↔ DTO conversions
│   │   └── test/                     # 🧪 Application logic tests (fast)
│   │
│   ├── Freetool.Infrastructure/      # 🔌 External system integrations
│   │   ├── src/
│   │   │   ├── Database/             # Data persistence layer
│   │   │   │   ├── FreetoolDbContext.fs # Entity Framework context
│   │   │   │   ├── UserEntity.fs     # Database entity mappings
│   │   │   │   ├── Persistence.fs    # Database migration utilities
│   │   │   │   ├── Repositories/     # Repository implementations
│   │   │   │   │   └── UserRepository.fs # User repository implementation
│   │   │   │   └── Migrations/       # Database schema migrations
│   │   │   │       └── DatabaseUpgradeScripts.DBUP.001_CreateUsersTable.sql
│   │   │   ├── ExternalServices/     # Third-party service integrations
│   │   │   │   ├── EmailService.fs   # SMTP email implementation
│   │   │   │   ├── HttpClientService.fs # HTTP client for external APIs
│   │   │   │   └── CacheService.fs   # Redis/in-memory caching
│   │   │   ├── Security/             # Authentication and authorization
│   │   │   │   ├── JwtTokenService.fs # JWT token creation/validation
│   │   │   │   ├── PasswordService.fs # Password hashing (bcrypt)
│   │   │   │   └── AuthorizationService.fs
│   │   │   └── Configuration/        # Infrastructure configuration
│   │   └── test/                     # 🧪 Infrastructure tests
│   │
│   └── Freetool.Api/                 # 🌐 HTTP API (outermost layer)
│       ├── src/
│       │   ├── Controllers/          # ASP.NET Core controllers
│       │   │   ├── UserController.fs # User management endpoints
│       │   │   ├── ToolController.fs # Tool CRUD endpoints
│       │   │   ├── DashboardController.fs
│       │   │   └── AuditController.fs # Audit log retrieval
│       │   ├── Middleware/           # HTTP middleware pipeline
│       │   │   ├── AuthenticationMiddleware.fs
│       │   │   ├── AuthorizationMiddleware.fs
│       │   │   ├── AuditMiddleware.fs # Request/response audit logging
│       │   │   └── ErrorHandlingMiddleware.fs
│       │   ├── Models/               # HTTP request/response models
│       │   ├── Program.fs            # Application entry point & DI configuration
│       │   ├── appsettings.json      # Production configuration
│       │   ├── appsettings.Development.json # Development configuration
│       │   └── Properties/
│       │       └── launchSettings.json # Launch profiles
│       └── test/                     # 🔗 End-to-end integration tests
│
└── docs/                             # 📚 Additional documentation
```

## 🚀 Getting Started

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Git](https://git-scm.com/)

### Database Setup

This project uses **SQLite** with **DBUp** for database migrations. SQLite is a lightweight, file-based database that works perfectly on Windows, macOS, and Linux with zero configuration required.

### Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/freetool.git
   cd freetool
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Install frontend dependencies**
   ```bash
   cd www && npm install && cd ..
   ```

4. **Configure database (optional)**
   The default configuration uses SQLite with a local file. The database file `freetool.db` will be created automatically in the API project directory. If needed, edit `src/Freetool.Api/appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Data Source=freetool.db"
     }
   }
   ```

5. **Start the application**
   ```bash
   docker-compose up --build
   ```

   **The database will be created automatically on first run!** The application uses DBUp to:
   - Create the database if it doesn't exist
   - Run all migration scripts automatically
   - Display migration progress in the console

5. **Access the API**
   - API: http://localhost:5000
   - Swagger UI: http://localhost:5001/swagger
   - OTEL traces: http://localhost:18888/

### Quick Test

Once the application is running, you can test the User API:

1. **Open Swagger UI** at http://localhost:5001/swagger
2. **Create a user** using the `POST /user` endpoint:
   ```json
   {
     "name": "John Doe",
     "email": "john.doe@example.com",
     "profilePicUrl": "https://example.com/profile.jpg"
   }
   ```
3. **Get users** using the `GET /user` endpoint
4. **Get user by ID** using the `GET /user/{id}` endpoint with the returned ID

The API supports full CRUD operations:
- `POST /user` - Create a new user
- `GET /user/{id}` - Get user by ID
- `GET /user/email/{email}` - Get user by email
- `GET /user?skip=0&take=10` - Get paginated list of users
- `PUT /user/{id}/name` - Update user name
- `PUT /user/{id}/email` - Update user email
- `PUT /user/{id}/profile-picture` - Set user profile picture
- `DELETE /user/{id}/profile-picture` - Remove user profile picture
- `DELETE /user/{id}` - Delete user

## 🧑‍💻 Local Development (Dev Mode)

For local development without Google IAP, Freetool provides a **Dev Mode** that bypasses authentication and lets you impersonate different users to test permissions.

### Production vs Dev Mode

| Aspect | Production Mode | Dev Mode |
|--------|----------------|----------|
| **Authentication** | Google IAP identity headers | `X-Dev-User-Id` header |
| **User identity** | Automatic from IAP | Manual selection via UI dropdown |
| **Backend port** | 5001 | 5002 |
| **Frontend port** | (served by backend) | 8081 |
| **Database** | `freetool-db` volume | `freetool-dev-db` volume (isolated) |
| **Docker command** | `docker-compose up` | `docker-compose -f docker-compose.dev.yml up` |

### Starting Dev Mode

From the project root, run:

```bash
docker-compose -f docker-compose.dev.yml up --build
```

This starts:
- **Frontend** at http://localhost:8081
- **Backend API** at http://localhost:5002
- **OpenFGA** at http://localhost:8090
- **Aspire Dashboard** (OTEL) at http://localhost:18888

Open http://localhost:8081 and you'll see a yellow **"DEVELOPMENT MODE"** banner at the top of the page and a user switcher dropdown in the header.

### Seeded Test Users

Dev mode automatically creates 4 test users with different permission levels:

| User | Email | Role | Permissions |
|------|-------|------|-------------|
| **Org Admin** | `admin@test.local` | Organization Admin | All permissions on all spaces |
| **Space Moderator** | `moderator@test.local` | Space Moderator | All permissions on "Test Space" |
| **Regular Member** | `member@test.local` | Space Member | Only `run_app` on "Test Space" |
| **No Permissions** | `noperm@test.local` | Space Member | No permissions (member only) |

### Seeded Test Data

Dev mode also creates sample data to work with:

- **Space**: "Test Space"
- **Resource**: "Sample API" (GET https://httpbin.org/get)
- **Folder**: "Sample Folder"
- **App**: "Hello World" in Sample Folder

### How User Switching Works

1. Select a user from the dropdown in the header
2. The page refreshes automatically
3. All subsequent API requests include the `X-Dev-User-Id` header
4. The backend uses this header to determine the current user's identity and permissions

This lets you test the app from different users' perspectives - for example, verifying that a member without `create_app` permission can't create apps, while a moderator can.

### Dev Mode API Endpoints

Dev mode exposes additional endpoints:

- `GET /dev/mode` - Returns `{ devMode: true }` (useful for frontend detection)
- `GET /dev/users` - Returns list of all users for the switcher dropdown

These endpoints return 404 in production mode.

### Database Migrations

This project uses [DBUp](https://dbup.readthedocs.io/) for database migrations instead of Entity Framework migrations. This gives you full control over your SQL scripts.

#### Adding New Migrations

1. **Create a new SQL script** in `src/Freetool.Infrastructure/src/Database/Migrations/`
   - File naming convention: `DatabaseUpgradeScripts.DBUP.{number}_{description}.sql`
   - Example: `DatabaseUpgradeScripts.DBUP.002_AddUserPreferencesTable.sql`

2. **Add the script to the project file** as an embedded resource:
   ```xml
   <EmbeddedResource Include="src/Database/Migrations/DatabaseUpgradeScripts.DBUP.002_AddUserPreferencesTable.sql" />
   ```

3. **Restart the application** - DBUp will automatically detect and run new scripts

#### Example Migration Script
```sql
-- DatabaseUpgradeScripts.DBUP.002_AddUserPreferencesTable.sql
CREATE TABLE UserPreferences (
    Id TEXT NOT NULL PRIMARY KEY,
    UserId TEXT NOT NULL,
    Theme TEXT NOT NULL DEFAULT 'Light',
    Language TEXT NOT NULL DEFAULT 'en',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);
```

#### Migration Benefits
- **Version control friendly**: SQL scripts are checked into source control
- **Database agnostic**: Easy to switch between SQL Server, SQLite, PostgreSQL, etc.
- **Full SQL control**: Write optimized SQL for complex migrations
- **Rollback support**: Create explicit down migration scripts when needed
- **Team collaboration**: No merge conflicts with migration files

## 🧪 Testing Strategy

### Unit Tests (Fast & Isolated)
- **Domain Layer**: Test business rules, entity behavior, and domain services
- **Application Layer**: Test use case orchestration and validation logic
- Run with: `dotnet test src/Freetool.Domain/test src/Freetool.Application/test`

### Integration Tests (Realistic & Comprehensive)
- **Infrastructure Layer**: Test database repositories with real SQL Server
- **API Layer**: Test HTTP endpoints with full middleware pipeline
- Run with: `dotnet test src/Freetool.Infrastructure/test src/Freetool.Api/test`

### Test Organization Principles
- Tests are colocated with source code in each project's `test/` folder
- Domain and Application tests should be **fast** (milliseconds) and require **no external dependencies**
- Integration tests may be **slower** but provide **high confidence** in real-world scenarios
- Use test databases and mock external services in integration tests

## 🛠️ Development Workflow

### Adding New Features

1. **Start with Domain**: Define entities, value objects, and business rules
2. **Add Application Logic**: Create use cases and define interfaces
3. **Implement Infrastructure**: Build repository implementations and external service integrations
4. **Expose via API**: Create controllers and HTTP models
5. **Test Each Layer**: Unit tests for inner layers, integration tests for outer layers

### File Ordering in F# Projects

F# requires dependencies to be ordered correctly in `.fsproj` files. Always ensure:
- Value objects come before entities that use them
- Domain services come after the entities they operate on
- Interfaces are defined before their implementations

### Code Standards

- Follow F# naming conventions (PascalCase for types, camelCase for values)
- Write comprehensive unit tests for Domain and Application layers
- Add integration tests for Infrastructure and API changes
- Keep business logic pure and free of infrastructure concerns
- Use meaningful commit messages following [Conventional Commits](https://www.conventionalcommits.org/)

# 💻 Deploying (GKE + IAP)

Freetool is designed to run behind **Google Cloud IAP** at the root path (`/`) on the shared internal-tools GKE cluster.

Use the Kubernetes deployment flow in this repo:

- Foundation/bootstrap stack: `infra/foundation/opentofu/`
- App workload stack: `infra/opentofu/`
- App stack guide: `infra/opentofu/README.md`
- Migration runbook: `MIGRATE_TO_K8S.md`
- App deploy script: `scripts/deploy-app-from-tofu.sh`

## 🔐 Production Auth Flow (IAP + Google Directory DWD)

At runtime, authentication and authorization flow works like this:

1. User accesses the app through the HTTPS load balancer and Kubernetes ingress protected by IAP.
2. IAP injects identity headers (`X-Goog-Authenticated-User-Email`, etc.) and JWT assertion header.
3. `IapAuthMiddleware` validates the IAP JWT assertion (`Auth:IAP:*` config).
4. Middleware extracts user identity and IAP group headers.
5. If Google Directory integration is enabled (`Auth:GoogleDirectory:Enabled=true`), `GoogleDirectoryIdentityService`:
   - obtains a token from service account credentials (ADC or credentials file),
   - optionally impersonates delegated admin user (`AdminUserEmail`) for domain-wide delegation,
   - calls Admin SDK Directory API (`users.get?projection=FULL`),
   - derives group keys from:
     - org unit path (`ou:/...`),
     - custom schemas (`custom:<schema>.<field>[:value]`).
6. Middleware merges IAP group keys + Directory-derived keys, then calls `IdentityProvisioningService`.
7. Provisioning ensures user exists and reconciles group-key to space mappings.
8. OpenFGA tuples determine effective access (org admin, moderator, member, app/resource/folder permissions).
9. Controllers enforce access via authorization checks.

## 🧾 Required Runtime Configuration

Minimum production settings:

```bash
Auth__IAP__ValidateJwt=true
Auth__IAP__JwtAudience=/projects/<project-number>/global/backendServices/<backend-service-id>
```

To enable Google Directory lookups:

```bash
Auth__GoogleDirectory__Enabled=true
Auth__GoogleDirectory__AdminUserEmail=<delegated-admin@your-domain>
Auth__GoogleDirectory__Scope=https://www.googleapis.com/auth/admin.directory.user.readonly
Auth__GoogleDirectory__OrgUnitKeyPrefix=ou
Auth__GoogleDirectory__IncludeOrgUnitHierarchy=true
Auth__GoogleDirectory__CustomAttributeKeyPrefix=custom
Auth__GoogleDirectory__CredentialsFile=/var/run/secrets/app/google-directory-dwd-key.json
```

If `Auth__GoogleDirectory__Enabled` is missing/false, Directory API calls are skipped and OU/custom-schema mappings will not work.

## 🛠️ Quick Production Verification

1. Confirm the workload is rolled out:
   ```bash
   kubectl -n app-freetool rollout status statefulset/app
   ```
2. Confirm env vars in the running API container:
   ```bash
   kubectl -n app-freetool exec app-0 -c api -- printenv | grep -E 'Auth__IAP|Auth__GoogleDirectory'
   ```
3. Check the health endpoint over a port-forward:
   ```bash
   kubectl -n app-freetool port-forward pod/app-0 18080:8080
   curl -i http://127.0.0.1:18080/healthy
   ```
4. Check logs for Directory failures:
   ```bash
   kubectl -n app-freetool logs app-0 -c api --since=30m | grep -E "Failed to obtain Google Directory access token|Google Directory lookup failed|lookup failed unexpectedly"
   ```
5. Smoke test through the public URL:
   ```bash
   curl -i https://<your-host>/user/me
   ```

# 📄 License & 🙏 Acknowledgements

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

- Inspired by [Retool](https://retool.com/) and the need for open-source internal tooling
- Built with [F#](https://fsharp.org/) and [ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/)
- Architecture influenced by [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html) and [Onion Architecture](https://jeffreypalermo.com/2008/07/the-onion-architecture-part-1/) principles
