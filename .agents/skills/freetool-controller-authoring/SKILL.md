---
name: freetool-controller-authoring
description: Create or update Freetool ASP.NET Core API controllers in F# using the established command-handler, authorization, error-mapping, DTO validation, and JSON converter patterns. Use when adding a new controller, adding endpoints to an existing controller, defining request/response DTOs for API transport, or introducing custom JSON conversion for DTO/domain boundary types.
---

# Freetool Controller Authoring

## Workflow

1. Define or update DTOs in `src/Freetool.Application/src/DTOs/`.
2. Add/update command + handler logic in Application layer.
3. Implement controller endpoints in `src/Freetool.Api/src/Controllers/`.
4. Register handler + traced `ICommandHandler<_, _>` in `src/Freetool.Api/src/Program.fs`.
5. Register JSON converters when needed in `Program.fs` + `DTOs/JsonConverters.fs`.
6. Add file order entries in relevant `.fsproj` files.

## Controller Pattern

### 1) Class shape

Use this structure for command-driven entities (`User`, `App`, `Folder`, `Resource`, `Space`, `Trash`):

```fsharp
namespace Freetool.Api.Controllers

open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Domain
open Freetool.Application.DTOs
open Freetool.Application.Commands
open Freetool.Application.Interfaces

[<ApiController>]
[<Route("widget")>]
type WidgetController
    (
        commandHandler: ICommandHandler<WidgetCommand, WidgetCommandResult>,
        authorizationService: IAuthorizationService
    ) =
    inherit AuthenticatedControllerBase()
```

Use `AuthenticatedControllerBase` when endpoint logic depends on `CurrentUserId`.

### 2) Endpoint shape

Use this template for most mutating endpoints:

```fsharp
[<HttpPost>]
[<ProducesResponseType(typeof<WidgetData>, StatusCodes.Status201Created)>]
[<ProducesResponseType(StatusCodes.Status400BadRequest)>]
[<ProducesResponseType(StatusCodes.Status403Forbidden)>]
[<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
member this.CreateWidget([<FromBody>] createDto: CreateWidgetDto) : Task<IActionResult> =
    task {
        let userId = this.CurrentUserId

        // Optional: authorization check before command execution
        let! hasPermission =
            authorizationService.CheckPermissionAsync
                (User(userId.Value.ToString()))
                WidgetCreate
                (SpaceObject createDto.SpaceId)

        if not hasPermission then
            return
                this.StatusCode(
                    403,
                    {| error = "Forbidden"
                       message = "You do not have permission to create widgets in this space" |}
                )
                :> IActionResult
        else
            let! result = commandHandler.HandleCommand(CreateWidget(userId, createDto))

            return
                match result with
                | Ok(WidgetResult widgetDto) ->
                    this.CreatedAtAction(nameof this.GetWidgetById, {| id = widgetDto.Id |}, widgetDto)
                    :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }
```

### 3) DomainError mapping

Keep a local helper (`HandleDomainError`) that maps all `DomainError` cases:

```fsharp
member private this.HandleDomainError(error: DomainError) : IActionResult =
    match error with
    | ValidationError message ->
        this.BadRequest
            {| error = "Validation failed"
               message = message |}
        :> IActionResult
    | NotFound message ->
        this.NotFound
            {| error = "Resource not found"
               message = message |}
        :> IActionResult
    | Conflict message ->
        this.Conflict
            {| error = "Conflict"
               message = message |}
        :> IActionResult
    | InvalidOperation message ->
        this.UnprocessableEntity
            {| error = "Invalid operation"
               message = message |}
        :> IActionResult
```

### 4) ID parsing and pagination

Follow existing conventions:
- Validate string IDs early with `Guid.TryParse` and return `ValidationError "Invalid ... ID format"`.
- Normalize pagination consistently:
  - `skip < 0 -> 0`
  - `take <= 0 -> 50`
  - `take > 100 -> 100`

### 5) Authorization conventions

- Check permissions at controller edge using `IAuthorizationService` and OpenFGA relations.
- Keep permission checks explicit per action (`Create`, `Edit`, `Delete`, `Run`).
- Return `403` with structured body:

```fsharp
{| error = "Forbidden"; message = "..." |}
```

- Prefer helper methods for repeated checks (`IsOrganizationAdmin`, `CheckAuthorization`, `GetSpaceIdFrom...`).

### 6) Sanitization pattern

When returning sensitive entities (`App`, `Resource`), sanitize response DTOs before returning:

```fsharp
let sanitized = ResponseSanitizer.sanitizeApp appDto
return this.Ok(sanitized) :> IActionResult
```

## DTO Expectations

DTOs are transport contracts only (API boundary), not domain models.

### Required rules

- Place DTOs in `src/Freetool.Application/src/DTOs/*.fs`.
- Use `[<Required>]`, `[<StringLength>]`, `[<EmailAddress>]`, `[<Url>]`, and `OptionalStringLengthAttribute` for request validation.
- Keep IDs as `string` in request DTOs; parse to value objects (`SpaceId`, `FolderId`, etc.) in controller/mapper/handler boundary.
- Use `string option` for optional transport fields.
- Keep response DTOs serializable with primitive/DTO fields, not domain aggregates.
- Reuse `ValidationConstants` for lengths/error messages when applicable.

### DTO examples

```fsharp
type UpdateWidgetNameDto =
    { [<Required>]
      [<StringLength(ValidationConstants.NameMaxLength,
                     MinimumLength = ValidationConstants.NameMinLength,
                     ErrorMessage = ValidationConstants.NameErrorMessage)>]
      Name: string }
```

```fsharp
type CreateWidgetDto =
    { [<Required>]
      Name: string

      [<Required>]
      SpaceId: string

      Description: string option }
```

## JSON Converter Expectations

### When to add a converter

Add a converter when a field type is not represented as plain JSON primitives in the desired API contract, especially for:
- Domain value objects or constrained types (`HttpMethod`, `ResourceKind`, `DatabaseEngine`, etc.).
- F# unions that need stable wire format.
- `string option` fields where empty string/null normalization is required.

### Where to add converter logic

1. Implement converter in `src/Freetool.Application/src/DTOs/JsonConverters.fs`.
2. Register converter globally in `src/Freetool.Api/src/Program.fs` `AddJsonOptions`:

```fsharp
options.JsonSerializerOptions.Converters.Add(HttpMethodConverter())
options.JsonSerializerOptions.Converters.Add(FolderLocationConverter())
options.JsonSerializerOptions.Converters.Add(JsonFSharpConverter(allowOverride = true))
```

3. Optionally apply converter to specific DTO property using `[<JsonConverter(typeof<...>)>]`.

### Property-level converter usage

Use for targeted option handling:

```fsharp
type CurrentUserDto =
    { Id: string
      Name: string
      [<JsonConverter(typeof<StringOptionConverter>)>]
      ProfilePicUrl: string option }
```

### F# DU payload format requirement

For DTO unions, prefer explicit case-based format. Existing pattern:

```fsharp
[<JsonFSharpConverter(UnionTagName = "case", UnionFieldsName = "fields")>]
type InputTypeDto =
    | Email
    | Text of MaxLength: int
```

This must deserialize from `{ "case": "Text", "fields": [100] }` style payloads.

## DI + Tracing Wiring Checklist

For command-backed controllers, wire handler + tracing in `src/Freetool.Api/src/Program.fs`:

```fsharp
builder.Services.AddScoped<WidgetHandler>() |> ignore

builder.Services.AddScoped<ICommandHandler<WidgetCommand, WidgetCommandResult>>(fun serviceProvider ->
    let widgetHandler = serviceProvider.GetRequiredService<WidgetHandler>()
    let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
    AutoTracing.createTracingDecorator "widget" widgetHandler activitySource)
|> ignore
```

Also register any new repositories/services needed by the controller or handler.

## F# File Ordering Checklist

When adding new files, update compile order in:
- `src/Freetool.Api/Freetool.Api.fsproj`
- `src/Freetool.Application/Freetool.Application.fsproj` (if DTO/converter files are added)

Keep dependencies before dependents (e.g., DTO type files before files that consume them).

## Pre-merge Verification

```bash
dotnet build Freetool.sln -c Release
dotnet test Freetool.sln
```

For API shape changes, refresh and review the OpenAPI contract:

```bash
# From repo root, with API running
curl http://localhost:5001/swagger/v1/swagger.json > openapi.spec.json
```
