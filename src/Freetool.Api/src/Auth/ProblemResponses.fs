namespace Freetool.Api.Auth

open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc

[<RequireQualifiedAccess>]
module ProblemResponses =
    let write
        (context: HttpContext)
        (status: int)
        (code: string)
        (title: string)
        (detail: string)
        (extensions: (string * obj) list)
        : Task =
        let problem = ProblemDetails()
        problem.Status <- status
        problem.Title <- title
        problem.Detail <- detail
        problem.Type <- $"https://wonderly.info/problems/{code}"
        problem.Extensions["code"] <- code

        for key, value in extensions do
            problem.Extensions[key] <- value

        let body = JsonSerializer.Serialize(problem)
        context.Response.StatusCode <- status
        context.Response.ContentType <- "application/problem+json"
        context.Response.WriteAsync(body)
