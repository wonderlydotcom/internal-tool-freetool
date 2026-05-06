namespace Freetool.Infrastructure.Tests

open System
open Oxpecker.ViewEngine
open Xunit
open Freetool.Api.Ui
open Freetool.Application.DTOs
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

    let private allPermissions: SpacePermissionsDto = {
        CreateResource = true
        EditResource = true
        DeleteResource = true
        CreateApp = true
        EditApp = true
        DeleteApp = true
        RunApp = true
        CreateDashboard = true
        EditDashboard = true
        DeleteDashboard = true
        RunDashboard = true
        CreateFolder = true
        EditFolder = true
        DeleteFolder = true
    }

    let private actionContext = {
        Permissions = allPermissions
        ModeratorDisplayName = Some "Moderator (moderator@test.local)"
        OrgAdminDisplayNames = [ "Admin (admin@test.local)" ]
        IsOrgAdmin = false
        IsSpaceModerator = false
    }

    let private actionContextWith permissions = {
        actionContext with
            Permissions = permissions
    }

    let private sampleFolder (space: SpaceData) = {
        Id = FolderId.NewId()
        Name = FolderName.Create(Some "Ops") |> unwrap
        ParentId = None
        SpaceId = space.Id
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
        Children = []
    }

    let private sampleUser id name email = {
        Id = id
        Name = name
        Email = email
        ProfilePicUrl = None
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
        InvitedAt = None
    }

    let private sampleMember user isModerator isOrgAdmin = {
        UserId = user.Id.Value.ToString()
        UserName = user.Name
        UserEmail = user.Email
        ProfilePicUrl = user.ProfilePicUrl
        IsModerator = isModerator
        IsOrgAdmin = isOrgAdmin
        Permissions = allPermissions
    }

    let private htmlSliceFromTo (startText: string) (endText: string) (html: string) =
        let startIndex = html.IndexOf(startText, StringComparison.Ordinal)
        Assert.True(startIndex >= 0, $"Expected HTML to contain '{startText}'.")
        let endIndex = html.IndexOf(endText, startIndex, StringComparison.Ordinal)
        Assert.True(endIndex >= 0, $"Expected HTML after '{startText}' to contain '{endText}'.")
        html.Substring(startIndex, endIndex - startIndex)

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

        let html =
            Views.runAppPage "token" (sampleSpace ()) [] app None actionContext
            |> Render.toString

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

    [<Fact>]
    let ``Run app page uses app scoped navigation instead of space navigation`` () =
        let space = sampleSpace ()
        let app = sampleApp []
        let aid = app.Id.Value.ToString()

        let html =
            Views.runAppPage "token" space [] app None actionContext |> Render.toString

        Assert.Contains("aria-label=\"App navigation\"", html)
        Assert.Contains("aria-current=\"page\">Run</a>", html)
        Assert.Contains($"/audit?scope=app&amp;appId={aid}", html)
        Assert.DoesNotContain("aria-label=\"Space navigation\"", html)
        Assert.DoesNotContain(">Resources</a>", html)
        Assert.DoesNotContain(">Settings</a>", html)
        Assert.DoesNotContain(">Trash</a>", html)

    [<Fact>]
    let ``Folder page hides space and app scoped navigation`` () =
        let space = sampleSpace ()
        let folder = sampleFolder space

        let html =
            Views.folderPage "token" space folder [ folder ] [] [] [] [] actionContext
            |> Render.toString

        Assert.DoesNotContain("aria-label=\"Space navigation\"", html)
        Assert.DoesNotContain("aria-label=\"App navigation\"", html)
        Assert.DoesNotContain(">Contents</a>", html)
        Assert.DoesNotContain(">Resources</a>", html)
        Assert.DoesNotContain(">Settings</a>", html)
        Assert.DoesNotContain(">Trash</a>", html)
        Assert.DoesNotContain(">Builder</a>", html)

    [<Fact>]
    let ``Run app page disables edit action when user cannot edit apps`` () =
        let permissions = { allPermissions with EditApp = false }

        let html =
            Views.runAppPage "token" (sampleSpace ()) [] (sampleApp []) None (actionContextWith permissions)
            |> Render.toString

        Assert.Contains("You do not have permission to edit apps. Ask Space Moderator", html)
        Assert.Contains("Edit app", html)
        Assert.Contains("disabled-action-control", html)

    [<Fact>]
    let ``Run app page disables runtime form when user cannot run apps`` () =
        let permissions = { allPermissions with RunApp = false }

        let html =
            Views.runAppPage "token" (sampleSpace ()) [] (sampleApp []) None (actionContextWith permissions)
            |> Render.toString

        Assert.Contains("You do not have permission to run apps. Ask Space Moderator", html)
        Assert.Contains("<fieldset class=\"form-fieldset\" disabled=\"disabled\">", html)
        Assert.Contains("run-submit-button disabled-action-control", html)

    [<Fact>]
    let ``Folder page shows disabled new app action when user cannot create apps`` () =
        let space = sampleSpace ()
        let folder = sampleFolder space

        let permissions = {
            allPermissions with
                CreateApp = false
        }

        let html =
            Views.folderPage "token" space folder [ folder ] [] [] [] [] (actionContextWith permissions)
            |> Render.toString

        Assert.Contains("New App", html)
        Assert.Contains("You do not have permission to create apps. Ask Space Moderator", html)
        Assert.DoesNotContain("Create a resource before adding apps to this folder.", html)

    [<Fact>]
    let ``Settings page is read only for non-admin non-moderator users`` () =
        let space = sampleSpace ()
        let users = [ sampleUser space.ModeratorUserId "Moderator" "moderator@test.local" ]

        let readOnlyContext = {
            actionContext with
                IsOrgAdmin = false
                IsSpaceModerator = false
        }

        let html =
            Views.settingsPage "token" space users [] allPermissions readOnlyContext
            |> Render.toString

        Assert.Contains("Only space moderators and organization administrators can rename spaces", html)
        Assert.Contains("<fieldset class=\"form-fieldset\" disabled=\"disabled\">", html)
        Assert.Contains("Only organization administrators can delete spaces. Ask Org Admin", html)
        Assert.Contains("Delete space", html)

    [<Fact>]
    let ``Settings page add member dropdown excludes users already in the space`` () =
        let moderatorId = UserId.NewId()
        let memberId = UserId.NewId()
        let candidateId = UserId.NewId()

        let space = {
            (sampleSpace ()) with
                ModeratorUserId = moderatorId
                MemberIds = [ memberId ]
        }

        let moderator = sampleUser moderatorId "Moderator" "moderator@test.local"
        let existingMember = sampleUser memberId "Member" "member@test.local"
        let candidate = sampleUser candidateId "Candidate" "candidate@test.local"

        let managerContext = {
            actionContext with
                IsSpaceModerator = true
        }

        let html =
            Views.settingsPage
                "token"
                space
                [ moderator; existingMember; candidate ]
                [ sampleMember moderator true false; sampleMember existingMember false false ]
                allPermissions
                managerContext
            |> Render.toString

        let addMemberSelect =
            htmlSliceFromTo "data-add-member-select=\"true\"" "</select>" html

        Assert.Contains($"value=\"{candidateId.Value}\"", addMemberSelect)
        Assert.Contains("Candidate (candidate@test.local)", addMemberSelect)
        Assert.DoesNotContain($"value=\"{memberId.Value}\"", addMemberSelect)
        Assert.DoesNotContain($"value=\"{moderatorId.Value}\"", addMemberSelect)
        Assert.Contains("data-add-member-submit=\"true\" disabled=\"disabled\"", html)

    [<Fact>]
    let ``Settings page add member form is disabled when no users can be added`` () =
        let moderatorId = UserId.NewId()
        let memberId = UserId.NewId()

        let space = {
            (sampleSpace ()) with
                ModeratorUserId = moderatorId
                MemberIds = [ memberId ]
        }

        let moderator = sampleUser moderatorId "Moderator" "moderator@test.local"
        let existingMember = sampleUser memberId "Member" "member@test.local"

        let managerContext = {
            actionContext with
                IsSpaceModerator = true
        }

        let html =
            Views.settingsPage
                "token"
                space
                [ moderator; existingMember ]
                [ sampleMember moderator true false; sampleMember existingMember false false ]
                allPermissions
                managerContext
            |> Render.toString

        let addMemberSelect =
            htmlSliceFromTo "data-add-member-select=\"true\"" "</select>" html

        Assert.Contains("disabled=\"disabled\"", addMemberSelect)
        Assert.Contains("No users available to add", addMemberSelect)
        Assert.Contains("data-add-member-submit=\"true\" disabled=\"disabled\"", html)