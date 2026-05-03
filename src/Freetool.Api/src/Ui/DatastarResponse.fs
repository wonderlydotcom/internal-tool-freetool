namespace Freetool.Api.Ui

open System.Text.Json
open Microsoft.AspNetCore.Http
open Oxpecker.ViewEngine
open StarFederation.Datastar.FSharp

type DatastarPatch = {
    Selector: string
    View: HtmlElement
    PatchMode: ElementPatchMode
}

module DatastarResponse =
    let private render (view: HtmlElement) = Render.toString view

    let private options patch = {
        PatchElementsOptions.Defaults with
            Selector = ValueSome patch.Selector
            PatchMode = patch.PatchMode
    }

    let patchMany (ctx: HttpContext) (patches: DatastarPatch list) = task {
        do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)

        for patch in patches do
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, render patch.View, options patch)
    }

    let patchElements ctx selector view =
        patchMany ctx [
            {
                Selector = selector
                View = view
                PatchMode = ElementPatchMode.Outer
            }
        ]

    let replaceElements ctx selector view =
        patchMany ctx [
            {
                Selector = selector
                View = view
                PatchMode = ElementPatchMode.Replace
            }
        ]

    let redirect (ctx: HttpContext) url = task {
        do! ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)

        do!
            ServerSentEventGenerator.ExecuteScriptAsync(
                ctx.Response,
                $"window.location.href = {JsonSerializer.Serialize(url)};"
            )
    }

    let badRequest (ctx: HttpContext) message = task {
        ctx.Response.StatusCode <- StatusCodes.Status400BadRequest
        ctx.Response.ContentType <- "text/plain; charset=utf-8"
        do! ctx.Response.WriteAsync(message)
    }

    let isDatastarRequest (ctx: HttpContext) = UiContext.isDatastarRequest ctx