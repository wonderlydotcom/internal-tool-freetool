namespace Freetool.Infrastructure.Tests

open System
open Oxpecker.ViewEngine
open Xunit
open Freetool.Api.Ui
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects

module UiRunAppViewTests =
    let private unwrap result =
        match result with
        | Ok value -> value
        | Error error -> failwithf "Expected Ok, got %A" error

    let private sampleSpace () = {
        Id = SpaceId.NewId()
        Name = "Support"
        ModeratorUserId = UserId.NewId()
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
        MemberIds = []
    }

    let private sampleApp inputs = {
        Id = AppId.NewId()
        Name = "Refunds and Cancel"
        FolderId = FolderId.NewId()
        ResourceId = ResourceId.NewId()
        HttpMethod = HttpMethod.Post
        Inputs = inputs
        UrlPath = None
        UrlParameters = []
        Headers = []
        Body = []
        UseDynamicJsonBody = false
        SqlConfig = None
        Description = None
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    [<Fact>]
    let ``Run app page renders configured choice inputs as selects`` () =
        let tableType = InputType.MultiText(100, [ "users"; "subscriptions" ]) |> unwrap

        let refundType =
            InputType.Radio [
                {
                    Value = "full"
                    Label = Some "Full refund"
                }
                {
                    Value = "prorated"
                    Label = Some "Pro-rated refund"
                }
            ]
            |> unwrap

        let cancelType =
            InputType.Radio [
                {
                    Value = "immediate"
                    Label = Some "Immediately"
                }
                {
                    Value = "end_of_cycle"
                    Label = Some "End of billing cycle"
                }
            ]
            |> unwrap

        let app =
            sampleApp [
                {
                    Title = "Database table"
                    Description = Some "Choose which production table to inspect."
                    Type = tableType
                    Required = true
                    DefaultValue = None
                }
                {
                    Title = "Refund type"
                    Description = None
                    Type = refundType
                    Required = true
                    DefaultValue = None
                }
                {
                    Title = "Cancel subscription"
                    Description = None
                    Type = cancelType
                    Required = true
                    DefaultValue = DefaultValue.Create(cancelType, "end_of_cycle") |> unwrap |> Some
                }
            ]

        let html = Views.runAppPage "token" (sampleSpace ()) [] app None |> Render.toString

        Assert.Contains("<select name=\"Database table\"", html)
        Assert.Contains("aria-label=\"Database table\"", html)
        Assert.Contains("<option value=\"users\">users</option>", html)
        Assert.Contains("<option value=\"subscriptions\">subscriptions</option>", html)
        Assert.Contains("<select name=\"Refund type\"", html)
        Assert.Contains("aria-label=\"Refund type\"", html)
        Assert.Contains("<option value=\"full\">Full refund</option>", html)
        Assert.Contains("<option value=\"prorated\">Pro-rated refund</option>", html)
        Assert.Contains("<select name=\"Cancel subscription\"", html)
        Assert.Contains("aria-label=\"Cancel subscription\"", html)
        Assert.Contains("<option value=\"end_of_cycle\" selected=\"selected\">End of billing cycle</option>", html)