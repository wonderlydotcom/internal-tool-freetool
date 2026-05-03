namespace Freetool.Api.Ui

open System
open System.Text.Json
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Interfaces

module UiFormat =
    let id (value: Guid) = value.ToString()

    let dateTime (value: DateTime) =
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")

    let optionText fallback value = value |> Option.defaultValue fallback

    let domainError =
        function
        | ValidationError message
        | NotFound message
        | Conflict message
        | InvalidOperation message -> message

    let flashFromQuery (ctx: Microsoft.AspNetCore.Http.HttpContext) =
        let kind = ctx.Request.Query["flash"].ToString()
        let message = ctx.Request.Query["message"].ToString()

        if String.IsNullOrWhiteSpace message then
            None
        else
            let tone =
                match kind.ToLowerInvariant() with
                | "success" -> FlashTone.Success
                | "error" -> FlashTone.Error
                | _ -> FlashTone.Info

            Some { Tone = tone; Message = message }

    let resourceKind (kind: ResourceKind) = kind.ToString()

    let inputType (inputType: InputType) =
        match inputType.Value with
        | InputTypeValue.Email -> "Email"
        | InputTypeValue.Date -> "Date"
        | InputTypeValue.Text maxLength -> $"Text ({maxLength} chars)"
        | InputTypeValue.Integer -> "Integer"
        | InputTypeValue.Boolean -> "Boolean"
        | InputTypeValue.Currency SupportedCurrency.USD -> "Currency (USD)"
        | InputTypeValue.MultiEmail _ -> "Email list"
        | InputTypeValue.MultiDate _ -> "Date list"
        | InputTypeValue.MultiText(maxLength, _) -> $"Text choices ({maxLength} chars)"
        | InputTypeValue.MultiInteger _ -> "Integer choices"
        | InputTypeValue.Radio options -> $"Radio ({List.length options} options)"

    let inputHtmlType (inputType: InputType) =
        match inputType.Value with
        | InputTypeValue.Email
        | InputTypeValue.MultiEmail _ -> "email"
        | InputTypeValue.Date
        | InputTypeValue.MultiDate _ -> "date"
        | InputTypeValue.Integer
        | InputTypeValue.MultiInteger _ -> "number"
        | InputTypeValue.Currency _ -> "number"
        | _ -> "text"

    let defaultValue (input: Input) =
        input.DefaultValue
        |> Option.map (fun defaultValue -> defaultValue.ToRawString())
        |> Option.defaultValue String.Empty

    let kvSummary pairs =
        if List.isEmpty pairs then
            "None"
        else
            pairs |> List.map (fun (pair: KeyValuePair) -> pair.Key) |> String.concat ", "

    let runStatus (status: RunStatus) = status.ToString()

    let tryFormatJson (value: string) =
        if String.IsNullOrWhiteSpace value then
            value
        else
            try
                use doc = JsonDocument.Parse(value)
                JsonSerializer.Serialize(doc.RootElement, JsonSerializerOptions(WriteIndented = true))
            with _ ->
                value

    let eventType (eventType: EventType) = EventTypeConverter.toString eventType

    let entityType (entityType: EntityType) = EntityTypeConverter.toString entityType

    let permissionLabel relation =
        match relation with
        | ResourceCreate -> "Create resources"
        | ResourceEdit -> "Edit resources"
        | ResourceDelete -> "Delete resources"
        | AppCreate -> "Create apps"
        | AppEdit -> "Edit apps"
        | AppDelete -> "Delete apps"
        | AppRun -> "Run apps"
        | DashboardCreate -> "Create dashboards"
        | DashboardEdit -> "Edit dashboards"
        | DashboardDelete -> "Delete dashboards"
        | DashboardRun -> "Run dashboards"
        | FolderCreate -> "Create folders"
        | FolderEdit -> "Edit folders"
        | FolderDelete -> "Delete folders"
        | SpaceRename -> "Rename space"
        | SpaceAddMember -> "Add members"
        | SpaceRemoveMember -> "Remove members"
        | SpaceDelete -> "Delete space"
        | _ -> relation.ToString()