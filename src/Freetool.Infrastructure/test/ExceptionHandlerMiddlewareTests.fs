module Freetool.Infrastructure.Tests.ExceptionHandlerMiddlewareTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Freetool.Api.Middleware
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Xunit

type StubHostEnvironment(envName: string) =
    interface IHostEnvironment with
        member _.EnvironmentName
            with get () = envName
            and set _ = ()

        member _.ApplicationName
            with get () = "TestApp"
            and set _ = ()

        member _.ContentRootPath
            with get () = ""
            and set _ = ()

        member _.ContentRootFileProvider
            with get () = null
            and set _ = ()

let createContext () =
    let context = DefaultHttpContext()
    context.Request.Method <- "GET"
    context.Request.Path <- PathString("/api/test")
    context.Response.Body <- new MemoryStream()
    context

let readResponseBody (context: HttpContext) =
    context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore

    use reader =
        new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen = true)

    reader.ReadToEnd()

let createRouteEndpoint template =
    RouteEndpoint(
        RequestDelegate(fun _ -> Task.CompletedTask),
        RoutePatternFactory.Parse(template),
        0,
        EndpointMetadataCollection.Empty,
        "test-endpoint"
    )

[<Fact>]
let ``No exception passes through normally`` () = task {
    let context = createContext ()
    let mutable nextInvoked = false

    let middleware =
        ExceptionHandlerMiddleware(
            RequestDelegate(fun _ ->
                nextInvoked <- true
                Task.CompletedTask),
            NullLogger<ExceptionHandlerMiddleware>.Instance,
            StubHostEnvironment("Production")
        )

    do! middleware.Invoke(context)

    Assert.True(nextInvoked)
    Assert.Equal(200, context.Response.StatusCode)
}

[<Fact>]
let ``Exception in production returns 500 without details`` () = task {
    let context = createContext ()

    let middleware =
        ExceptionHandlerMiddleware(
            RequestDelegate(fun _ -> raise (InvalidOperationException("Secret error"))),
            NullLogger<ExceptionHandlerMiddleware>.Instance,
            StubHostEnvironment("Production")
        )

    do! middleware.Invoke(context)

    Assert.Equal(500, context.Response.StatusCode)
    Assert.Equal("application/problem+json", context.Response.ContentType)

    let body = readResponseBody context
    let doc = JsonDocument.Parse(body)
    let root = doc.RootElement
    Assert.Equal(500, root.GetProperty("status").GetInt32())
    Assert.Equal("Internal Server Error", root.GetProperty("title").GetString())

    let hasDetail =
        match root.TryGetProperty("detail") with
        | true, v -> v.ValueKind <> JsonValueKind.Null
        | false, _ -> false

    Assert.False(hasDetail, "Production response should not include exception detail")
}

[<Fact>]
let ``Exception in development returns 500 with details and stack trace`` () = task {
    let context = createContext ()

    let middleware =
        ExceptionHandlerMiddleware(
            RequestDelegate(fun _ -> raise (InvalidOperationException("Dev error message"))),
            NullLogger<ExceptionHandlerMiddleware>.Instance,
            StubHostEnvironment("Development")
        )

    do! middleware.Invoke(context)

    Assert.Equal(500, context.Response.StatusCode)
    Assert.Equal("application/problem+json", context.Response.ContentType)

    let body = readResponseBody context
    let doc = JsonDocument.Parse(body)
    let root = doc.RootElement
    Assert.Equal(500, root.GetProperty("status").GetInt32())
    Assert.Equal("Internal Server Error", root.GetProperty("title").GetString())
    Assert.Equal("Dev error message", root.GetProperty("detail").GetString())
    Assert.True(root.TryGetProperty("stackTrace") |> fst, "Development response should include stack trace")
}

[<Fact>]
let ``Exception annotates current activity with stable error metadata`` () = task {
    let context = createContext ()
    context.SetEndpoint(createRouteEndpoint "/api/examples/{id}")

    use activity = new Activity("request")
    activity.Start() |> ignore

    let middleware =
        ExceptionHandlerMiddleware(
            RequestDelegate(fun _ -> raise (InvalidOperationException("Boom"))),
            NullLogger<ExceptionHandlerMiddleware>.Instance,
            StubHostEnvironment("Production")
        )

    do! middleware.Invoke(context)

    let routeTag = activity.GetTagItem("http.route") :?> string
    let errorGroupTag = activity.GetTagItem("error.group") :?> string
    let errorTypeTag = activity.GetTagItem("error.type") :?> string

    Assert.Equal(ActivityStatusCode.Error, activity.Status)
    Assert.Equal("/api/examples/{id}", routeTag)
    Assert.Equal("InvalidOperation", errorGroupTag)
    Assert.Equal("System.InvalidOperationException", errorTypeTag)

    let exceptionEvent =
        activity.Events
        |> Seq.tryFind (fun activityEvent -> activityEvent.Name = "exception")

    Assert.True(exceptionEvent.IsSome, "Exception should be recorded on the current activity")
}