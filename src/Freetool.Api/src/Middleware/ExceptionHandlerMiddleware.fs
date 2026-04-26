namespace Freetool.Api.Middleware

open System
open System.Diagnostics
open System.Text.Json
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open OpenTelemetry.Trace

module private ExceptionTelemetry =
    let routeTemplate (context: HttpContext) =
        match context.GetEndpoint() with
        | :? RouteEndpoint as endpoint when not (String.IsNullOrWhiteSpace(endpoint.RoutePattern.RawText)) ->
            endpoint.RoutePattern.RawText
        | _ -> string context.Request.Path

    let errorType (ex: exn) =
        let fullName = ex.GetType().FullName

        if String.IsNullOrWhiteSpace(fullName) then
            ex.GetType().Name
        else
            fullName

    let errorGroup (ex: exn) =
        let name = ex.GetType().Name

        if
            name.EndsWith("Exception", StringComparison.Ordinal)
            && name.Length > "Exception".Length
        then
            name.Substring(0, name.Length - "Exception".Length)
        else
            name

    let annotateCurrentActivity (routeTemplate: string) (errorGroup: string) (errorType: string) (ex: exn) =
        let activity = Activity.Current

        if not (isNull activity) then
            activity.SetStatus(ActivityStatusCode.Error, ex.Message) |> ignore
            activity.SetTag("http.route", routeTemplate) |> ignore
            activity.SetTag("error.group", errorGroup) |> ignore
            activity.SetTag("error.type", errorType) |> ignore
            activity.AddException(ex) |> ignore

type ExceptionHandlerMiddleware
    (next: RequestDelegate, logger: ILogger<ExceptionHandlerMiddleware>, env: IHostEnvironment) =

    member _.Invoke(context: HttpContext) = task {
        try
            do! next.Invoke(context)
        with ex ->
            let routeTemplate = ExceptionTelemetry.routeTemplate context
            let errorGroup = ExceptionTelemetry.errorGroup ex
            let errorType = ExceptionTelemetry.errorType ex

            ExceptionTelemetry.annotateCurrentActivity routeTemplate errorGroup errorType ex

            logger.LogError(
                ex,
                "Unhandled exception on {Method} {RouteTemplate} status_code={StatusCode} error_group={ErrorGroup} error_type={ErrorType}",
                context.Request.Method,
                routeTemplate,
                StatusCodes.Status500InternalServerError,
                errorGroup,
                errorType
            )

            context.Response.StatusCode <- StatusCodes.Status500InternalServerError
            context.Response.ContentType <- "application/problem+json"

            let problem = ProblemDetails(Status = 500, Title = "Internal Server Error")

            if env.IsDevelopment() then
                problem.Detail <- ex.Message
                problem.Extensions["stackTrace"] <- ex.StackTrace

            let options =
                JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

            do! JsonSerializer.SerializeAsync(context.Response.Body, problem, options)
    }