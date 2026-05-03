namespace Freetool.Api.Middleware

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open System
open System.Threading.Tasks
open System.Diagnostics
open Freetool.Api.Auth
open Freetool.Application.Interfaces
open Freetool.Domain.ValueObjects
open Freetool.Api.Tracing

type DevAuthMiddleware(next: RequestDelegate) =

    let DEV_USER_ID_HEADER = "X-Dev-User-Id"
    let DEV_USER_ID_COOKIE = "freetool_dev_user_id"

    let extractHeader (headerKey: string) (context: HttpContext) : string option =
        match context.Request.Headers.TryGetValue headerKey with
        | true, values when values.Count > 0 ->
            let value = values.[0]

            if String.IsNullOrWhiteSpace value then None else Some value
        | _ -> None

    let extractCookie (cookieKey: string) (context: HttpContext) : string option =
        match context.Request.Cookies.TryGetValue cookieKey with
        | true, value when not (String.IsNullOrWhiteSpace value) -> Some value
        | _ -> None

    let isStaticAssetPath (path: string) =
        path = "/favicon.ico"
        || path = "/placeholder.svg"
        || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/vendor/", StringComparison.OrdinalIgnoreCase)

    let isBypassedPath (path: string) =
        path = "/healthy"
        || path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
        || isStaticAssetPath path

    let isMachineApiPath (path: string) =
        path = "/__api/me"
        || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/admin/openfga", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/app", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/folder", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/org/identity/group-space-mappings", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/resource", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/space", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/trash", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/user", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/audit/events", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/audit/app/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/audit/user/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/audit/dashboard/", StringComparison.OrdinalIgnoreCase)

    let isHostedUiPath (path: string) =
        path = "/"
        || path = "/spaces"
        || path = "/spaces-list"
        || path = "/users"
        || path = "/audit"
        || path.StartsWith("/spaces/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/_ui/", StringComparison.OrdinalIgnoreCase)
        || not (isMachineApiPath path || isStaticAssetPath path)

    let redirectToDevPicker (context: HttpContext) =
        let returnUrl =
            context.Request.PathBase.ToString()
            + context.Request.Path.ToString()
            + context.Request.QueryString.ToString()

        let encoded =
            Uri.EscapeDataString(
                if String.IsNullOrWhiteSpace returnUrl then
                    "/spaces"
                else
                    returnUrl
            )

        context.Response.Redirect($"/dev/select-user?returnUrl={encoded}", false)
        context.Response.StatusCode <- StatusCodes.Status303SeeOther
        Task.CompletedTask

    member _.InvokeAsync(context: HttpContext) : Task = task {
        let currentActivity = Option.ofObj Activity.Current

        let path =
            context.Request.Path.Value |> Option.ofObj |> Option.defaultValue String.Empty

        // Header takes precedence for API tests/backward compatibility; UI page loads use the dev cookie.
        let userIdOption =
            extractHeader DEV_USER_ID_HEADER context
            |> Option.orElseWith (fun () -> extractCookie DEV_USER_ID_COOKIE context)

        if isBypassedPath path then
            do! next.Invoke context
        else
            match userIdOption with
            | None when isHostedUiPath path -> do! redirectToDevPicker context
            | None ->
                Tracing.addAttribute currentActivity "dev.auth.error" "missing_user_id"
                Tracing.addAttribute currentActivity "dev.auth.header" DEV_USER_ID_HEADER
                Tracing.addAttribute currentActivity "dev.auth.cookie" DEV_USER_ID_COOKIE
                Tracing.setSpanStatus currentActivity false (Some "Missing dev user identity")

                do!
                    ProblemResponses.write
                        context
                        StatusCodes.Status401Unauthorized
                        "missing_dev_user_id_header"
                        "Unauthorized"
                        $"Missing {DEV_USER_ID_HEADER} header or {DEV_USER_ID_COOKIE} cookie."
                        [ "header", DEV_USER_ID_HEADER; "cookie", DEV_USER_ID_COOKIE ]
            | Some userIdStr ->
                match Guid.TryParse userIdStr with
                | false, _ ->
                    Tracing.addAttribute currentActivity "dev.auth.error" "invalid_user_id_format"
                    Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                    Tracing.setSpanStatus currentActivity false (Some "Invalid user ID format")

                    do!
                        ProblemResponses.write
                            context
                            StatusCodes.Status401Unauthorized
                            "invalid_dev_user_id"
                            "Unauthorized"
                            "Invalid user ID format."
                            []
                | true, guid ->
                    let userId = UserId.FromGuid guid
                    let userRepository = context.RequestServices.GetRequiredService<IUserRepository>()
                    let! userOption = userRepository.GetByIdAsync userId

                    match userOption with
                    | None ->
                        Tracing.addAttribute currentActivity "dev.auth.error" "user_not_found"
                        Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                        Tracing.setSpanStatus currentActivity false (Some "User not found")

                        do!
                            ProblemResponses.write
                                context
                                StatusCodes.Status401Unauthorized
                                "dev_user_not_found"
                                "Unauthorized"
                                "User not found."
                                []
                    | Some user ->
                        context.Items.["UserId"] <- user.State.Id

                        RequestUserContext.set context {
                            UserId = Some user.State.Id
                            Name = Some user.State.Name
                            Email = user.State.Email
                            Profile = user.State.ProfilePicUrl
                            GroupKeys = []
                            AuthenticationSource = "development"
                        }

                        Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                        Tracing.addAttribute currentActivity "user.id" (user.State.Id.Value.ToString())
                        Tracing.addAttribute currentActivity "dev.auth.success" "true"
                        Tracing.setSpanStatus currentActivity true None
                        do! next.Invoke context
    }