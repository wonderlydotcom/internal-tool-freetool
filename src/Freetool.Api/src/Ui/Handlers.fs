namespace Freetool.Api.Ui

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Extensions
open Microsoft.Extensions.DependencyInjection
open Oxpecker
open Oxpecker.ViewEngine
open Freetool.Api.Controllers
open Freetool.Application.Commands
open Freetool.Application.DTOs
open Freetool.Application.Handlers
open Freetool.Application.Interfaces
open Freetool.Application.Mappers
open Freetool.Application.Services
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects

module Handlers =
    let private normalize (value: string) =
        if isNull value then String.Empty else value.Trim()

    let private optionalText value =
        let text = normalize value
        if String.IsNullOrWhiteSpace text then None else Some text

    let private parsePositiveInt fallback (value: string) =
        match Int32.TryParse value with
        | true, parsed when parsed > 0 -> parsed
        | _ -> fallback

    let private queryValue (ctx: HttpContext) key = ctx.Request.Query[key].ToString()

    let private queryPage ctx =
        queryValue ctx "page" |> parsePositiveInt 1

    let private queryOption ctx key = queryValue ctx key |> optionalText

    let private parseDateQuery
        (label: string)
        (endOfDay: bool)
        (value: string option)
        : Result<DateTime option, string> =
        match value with
        | None -> Ok None
        | Some text ->
            match DateTime.TryParse text with
            | true, parsed ->
                let date =
                    if endOfDay then
                        parsed.Date.AddDays(1.0).AddTicks(-1L)
                    else
                        parsed.Date

                Ok(Some date)
            | _ -> Error $"{label} must be a valid date."

    let private buildQueryHref (path: string) (pairs: (string * string option) list) =
        let query =
            pairs
            |> List.choose (fun (key, value) ->
                value
                |> Option.bind (fun text ->
                    if String.IsNullOrWhiteSpace text then
                        None
                    else
                        Some $"{Uri.EscapeDataString key}={Uri.EscapeDataString text}"))
            |> String.concat "&"

        if String.IsNullOrWhiteSpace query then
            path
        else
            $"{path}?{query}"

    let private form (ctx: HttpContext) = ctx.Request.ReadFormAsync()
    let private formValue (form: IFormCollection) key = form[key].ToString() |> normalize
    let private optionalFormValue form key = formValue form key |> optionalText

    let private formValuesRaw (form: IFormCollection) key =
        form[key] |> Seq.map string |> Seq.map normalize |> Seq.toList

    let private formValues (form: IFormCollection) key =
        formValuesRaw form key
        |> Seq.filter (String.IsNullOrWhiteSpace >> not)
        |> Seq.toList

    let private valueAt (values: string list) index =
        values |> List.tryItem index |> Option.defaultValue String.Empty

    let private keyValuePairs (form: IFormCollection) keyPrefix : KeyValuePairDto list =
        let keys = formValuesRaw form $"{keyPrefix}Key"
        let values = formValuesRaw form $"{keyPrefix}Value"
        let rowCount = max keys.Length values.Length

        [ 0 .. rowCount - 1 ]
        |> List.choose (fun index ->
            let key = valueAt keys index
            let value = valueAt values index

            if String.IsNullOrWhiteSpace key || String.IsNullOrWhiteSpace value then
                None
            else
                Some { Key = key; Value = value })

    let private parseOptionalInt (fieldLabel: string) (value: string option) : Result<int option, DomainError> =
        match value with
        | None -> Ok None
        | Some text ->
            match Int32.TryParse text with
            | true, parsed -> Ok(Some parsed)
            | _ -> Error(ValidationError $"{fieldLabel} must be a number")

    let private sqlQueryConfigFromForm (form: IFormCollection) : Result<SqlQueryConfigDto option, DomainError> =
        let mode = optionalFormValue form "SqlMode" |> Option.defaultValue "gui"
        let normalizedMode = mode.Trim().ToLowerInvariant()

        match normalizedMode with
        | "raw" ->
            Ok(
                Some {
                    Mode = "raw"
                    Table = None
                    Columns = []
                    Filters = []
                    Limit = None
                    OrderBy = []
                    RawSql = optionalFormValue form "RawSql" |> Option.orElse (Some "select 1")
                    RawSqlParams = keyValuePairs form "RawSqlParam"
                }
            )
        | "gui" ->
            match parseOptionalInt "SQL limit" (optionalFormValue form "SqlLimit") with
            | Error error -> Error error
            | Ok limit ->
                let columns = formValues form "SqlColumn" |> List.distinct
                let filterColumns = formValuesRaw form "SqlFilterColumn"
                let filterOperators = formValuesRaw form "SqlFilterOperator"
                let filterValues = formValuesRaw form "SqlFilterValue"

                let filterRowCount =
                    [ filterColumns.Length; filterOperators.Length; filterValues.Length ]
                    |> List.max

                let filters: SqlFilterDto list =
                    [ 0 .. filterRowCount - 1 ]
                    |> List.choose (fun index ->
                        let column = valueAt filterColumns index

                        if String.IsNullOrWhiteSpace column then
                            None
                        else
                            let op = optionalText (valueAt filterOperators index) |> Option.defaultValue "="

                            let value =
                                if
                                    op.Equals("IS NULL", StringComparison.OrdinalIgnoreCase)
                                    || op.Equals("IS NOT NULL", StringComparison.OrdinalIgnoreCase)
                                then
                                    None
                                else
                                    optionalText (valueAt filterValues index)

                            Some(
                                {
                                    Column = column
                                    Operator = op
                                    Value = value
                                }
                                : SqlFilterDto
                            ))

                let orderColumns = formValuesRaw form "SqlOrderByColumn"
                let orderDirections = formValuesRaw form "SqlOrderByDirection"
                let orderRowCount = max orderColumns.Length orderDirections.Length

                let orderBy: SqlOrderByDto list =
                    [ 0 .. orderRowCount - 1 ]
                    |> List.choose (fun index ->
                        let column = valueAt orderColumns index

                        if String.IsNullOrWhiteSpace column then
                            None
                        else
                            Some(
                                {
                                    Column = column
                                    Direction =
                                        optionalText (valueAt orderDirections index) |> Option.defaultValue "ASC"
                                }
                                : SqlOrderByDto
                            ))

                Ok(
                    Some {
                        Mode = "gui"
                        Table = optionalFormValue form "SqlTable"
                        Columns = columns
                        Filters = filters
                        Limit = limit
                        OrderBy = orderBy
                        RawSql = None
                        RawSqlParams = []
                    }
                )
        | _ -> Error(ValidationError $"Invalid SQL query mode: {mode}")

    let private sequenceResults (results: Result<'T, DomainError> list) : Result<'T list, DomainError> =
        let folder acc item =
            match acc, item with
            | Ok items, Ok item -> Ok(item :: items)
            | Error error, _ -> Error error
            | _, Error error -> Error error

        results |> List.fold folder (Ok []) |> Result.map List.rev

    let private parseBool fallback (value: string) =
        match Boolean.TryParse value with
        | true, parsed -> parsed
        | _ -> fallback

    let private splitCsv (value: string) =
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map normalize
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.toList

    let private radioOptionsFromCsv (value: string) : RadioOptionDto list =
        splitCsv value |> List.map (fun option -> { Value = option; Label = None })

    let private inputTypeFromForm (inputType: string) (config: string) : Result<InputTypeDto, DomainError> =
        let normalizedType = inputType.Trim().ToLowerInvariant()
        let normalizedConfig = normalize config

        match normalizedType with
        | "email" -> Ok InputTypeDto.Email
        | "date" -> Ok InputTypeDto.Date
        | "integer" -> Ok InputTypeDto.Integer
        | "boolean" -> Ok InputTypeDto.Boolean
        | "currency" -> Ok(InputTypeDto.Currency SupportedCurrencyDto.USD)
        | "radio" -> Ok(InputTypeDto.Radio(radioOptionsFromCsv normalizedConfig))
        | "multi-email" -> Ok(InputTypeDto.MultiEmail(splitCsv normalizedConfig))
        | "multi-date" -> Ok(InputTypeDto.MultiDate(splitCsv normalizedConfig))
        | "multi-integer" ->
            let parsed =
                splitCsv normalizedConfig
                |> List.map (fun value ->
                    match Int32.TryParse value with
                    | true, parsed -> Ok parsed
                    | _ -> Error(ValidationError $"Invalid integer input option: {value}"))

            parsed |> sequenceResults |> Result.map InputTypeDto.MultiInteger
        | "multi-text" ->
            let maxLength, valuesText =
                match normalizedConfig.Split('|', 2) with
                | [| length; values |] -> parsePositiveInt 100 length, values
                | _ -> 100, normalizedConfig

            Ok(InputTypeDto.MultiText(maxLength, splitCsv valuesText))
        | "text"
        | _ -> Ok(InputTypeDto.Text(parsePositiveInt 100 normalizedConfig))

    let private appInputs (form: IFormCollection) : Result<AppInputDto list, DomainError> =
        let titles = formValuesRaw form "InputTitle"
        let descriptions = formValuesRaw form "InputDescription"
        let types = formValuesRaw form "InputType"
        let requiredValues = formValuesRaw form "InputRequired"
        let defaultValues = formValuesRaw form "InputDefaultValue"
        let typeConfigs = formValuesRaw form "InputTypeConfig"

        let rowCount =
            [
                titles.Length
                descriptions.Length
                types.Length
                requiredValues.Length
                defaultValues.Length
                typeConfigs.Length
            ]
            |> List.max

        [ 0 .. rowCount - 1 ]
        |> List.choose (fun index ->
            let title = valueAt titles index

            if String.IsNullOrWhiteSpace title then
                None
            else
                let inputType = valueAt types index

                let isBoolean =
                    inputType.Trim().Equals("boolean", StringComparison.OrdinalIgnoreCase)

                let required =
                    if isBoolean then
                        true
                    else
                        valueAt requiredValues index |> parseBool false

                let defaultValue =
                    if required then
                        None
                    else
                        optionalText (valueAt defaultValues index)

                inputTypeFromForm inputType (valueAt typeConfigs index)
                |> Result.map (fun parsedType -> {
                    Input = {
                        Title = title
                        Description = optionalText (valueAt descriptions index)
                        Type = parsedType
                    }
                    Required = required
                    DefaultValue = defaultValue
                })
                |> Some)
        |> sequenceResults

    let private permissionFieldForUser (field: string) (userId: string) = $"{field}:{userId}"

    let private permissionsFromFormFields (fieldName: string -> string) (form: IFormCollection) : SpacePermissionsDto =
        let isChecked field =
            formValues form (fieldName field) |> List.isEmpty |> not

        {
            CreateResource = isChecked "CreateResource"
            EditResource = isChecked "EditResource"
            DeleteResource = isChecked "DeleteResource"
            CreateApp = isChecked "CreateApp"
            EditApp = isChecked "EditApp"
            DeleteApp = isChecked "DeleteApp"
            RunApp = isChecked "RunApp"
            CreateDashboard = isChecked "CreateDashboard"
            EditDashboard = isChecked "EditDashboard"
            DeleteDashboard = isChecked "DeleteDashboard"
            RunDashboard = isChecked "RunDashboard"
            CreateFolder = isChecked "CreateFolder"
            EditFolder = isChecked "EditFolder"
            DeleteFolder = isChecked "DeleteFolder"
        }

    let private permissionsFromForm (form: IFormCollection) : SpacePermissionsDto = permissionsFromFormFields id form

    let private permissionsForUserFromForm (form: IFormCollection) (userId: string) : SpacePermissionsDto =
        permissionsFromFormFields (fun field -> permissionFieldForUser field userId) form

    let private service<'T> (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<'T>()

    let private users (ctx: HttpContext) = service<IUserRepository> ctx
    let private spaces (ctx: HttpContext) = service<ISpaceRepository> ctx
    let private folders (ctx: HttpContext) = service<IFolderRepository> ctx
    let private apps (ctx: HttpContext) = service<IAppRepository> ctx
    let private dashboards (ctx: HttpContext) = service<IDashboardRepository> ctx
    let private resources (ctx: HttpContext) = service<IResourceRepository> ctx
    let private runs (ctx: HttpContext) = service<IRunRepository> ctx
    let private events (ctx: HttpContext) = service<IEventRepository> ctx
    let private eventEnhancement (ctx: HttpContext) = service<IEventEnhancementService> ctx
    let private sqlExecution (ctx: HttpContext) = service<ISqlExecutionService> ctx
    let private auth (ctx: HttpContext) = service<IAuthorizationService> ctx

    let private relationshipReader (ctx: HttpContext) =
        service<IAuthorizationRelationshipReader> ctx

    let private spaceHandler (ctx: HttpContext) =
        service<ICommandHandler<SpaceCommand, SpaceCommandResult>> ctx

    let private folderHandler (ctx: HttpContext) =
        service<ICommandHandler<FolderCommand, FolderCommandResult>> ctx

    let private appHandler (ctx: HttpContext) =
        service<ICommandHandler<AppCommand, AppCommandResult>> ctx

    let private dashboardHandler (ctx: HttpContext) =
        service<ICommandHandler<DashboardCommand, DashboardCommandResult>> ctx

    let private resourceHandler (ctx: HttpContext) =
        service<ICommandHandler<ResourceCommand, ResourceCommandResult>> ctx

    let private trashHandler (ctx: HttpContext) =
        service<ICommandHandler<TrashCommand, TrashCommandResult>> ctx

    let private writePage (ctx: HttpContext) (active: string) (title: string) (content: HtmlElement) = task {
        let! model = UiContext.layoutModel ctx active title (UiFormat.flashFromQuery ctx)
        return! ctx.WriteHtmlView(LayoutView.page model content)
    }

    let private writeStatus (ctx: HttpContext) (statusCode: int) (title: string) (message: string) = task {
        ctx.Response.StatusCode <- statusCode
        return! ctx.WriteHtmlView(LayoutView.statusPage title message None statusCode)
    }

    let private redirect (ctx: HttpContext) (url: string) = task {
        if DatastarResponse.isDatastarRequest ctx then
            return! DatastarResponse.redirect ctx url
        else
            ctx.Response.Redirect(url, false)
            ctx.Response.StatusCode <- StatusCodes.Status303SeeOther
    }

    let private redirectWithFlash (ctx: HttpContext) (url: string) (kind: string) (message: string) =
        let separator = if url.Contains "?" then "&" else "?"

        let target =
            $"{url}{separator}flash={Uri.EscapeDataString(kind)}&message={Uri.EscapeDataString(message)}"

        redirect ctx target

    let private redirectBackToSpace (ctx: HttpContext) (spaceId: string) (kind: string) (message: string) =
        redirectWithFlash ctx $"/spaces/{spaceId}" kind message

    let private validatePost (ctx: HttpContext) = UiContext.validateAntiforgery ctx

    let private trySpaceId (value: string) =
        match Guid.TryParse value with
        | true, guid -> Some(SpaceId.FromGuid guid)
        | _ -> None

    let private tryFolderId (value: string) =
        match Guid.TryParse value with
        | true, guid -> Some(FolderId.FromGuid guid)
        | _ -> None

    let private tryAppId (value: string) =
        match Guid.TryParse value with
        | true, guid -> Some(AppId.FromGuid guid)
        | _ -> None

    let private tryDashboardId (value: string) =
        match Guid.TryParse value with
        | true, guid -> Some(DashboardId.FromGuid guid)
        | _ -> None

    let private tryResourceId (value: string) =
        match Guid.TryParse value with
        | true, guid -> Some(ResourceId.FromGuid guid)
        | _ -> None

    let private isOrgAdmin (ctx: HttpContext) = task {
        let actor = UiContext.actorUserIdString ctx

        return!
            (auth ctx).CheckPermissionAsync
                (AuthSubject.User actor)
                AuthRelation.OrganizationAdmin
                (AuthObject.OrganizationObject "default")
    }

    let private isOrgAdminOrModerator (ctx: HttpContext) (space: SpaceData) = task {
        let! admin = isOrgAdmin ctx

        if admin then
            return true
        else
            let actor = UiContext.actorUserId ctx

            if space.ModeratorUserId = actor then
                return true
            else
                return!
                    (auth ctx).CheckPermissionAsync
                        (AuthSubject.User(actor.Value.ToString()))
                        AuthRelation.SpaceModerator
                        (AuthObject.SpaceObject(space.Id.Value.ToString()))
    }

    let private hasSpacePermission (ctx: HttpContext) (spaceId: SpaceId) (permission: AuthRelation) = task {
        return!
            SpacePermissionAuthorization.hasSpacePermissionWithModeratorFallback
                (auth ctx)
                (spaces ctx)
                (UiContext.actorUserId ctx)
                spaceId
                permission
    }

    let private getSpaceUiPermissions (ctx: HttpContext) (spaceId: SpaceId) : Task<SpacePermissionsDto> = task {
        let! createResource = hasSpacePermission ctx spaceId AuthRelation.ResourceCreate
        let! editResource = hasSpacePermission ctx spaceId AuthRelation.ResourceEdit
        let! deleteResource = hasSpacePermission ctx spaceId AuthRelation.ResourceDelete
        let! createApp = hasSpacePermission ctx spaceId AuthRelation.AppCreate
        let! editApp = hasSpacePermission ctx spaceId AuthRelation.AppEdit
        let! deleteApp = hasSpacePermission ctx spaceId AuthRelation.AppDelete
        let! runApp = hasSpacePermission ctx spaceId AuthRelation.AppRun
        let! createDashboard = hasSpacePermission ctx spaceId AuthRelation.DashboardCreate
        let! editDashboard = hasSpacePermission ctx spaceId AuthRelation.DashboardEdit
        let! deleteDashboard = hasSpacePermission ctx spaceId AuthRelation.DashboardDelete
        let! runDashboard = hasSpacePermission ctx spaceId AuthRelation.DashboardRun
        let! createFolder = hasSpacePermission ctx spaceId AuthRelation.FolderCreate
        let! editFolder = hasSpacePermission ctx spaceId AuthRelation.FolderEdit
        let! deleteFolder = hasSpacePermission ctx spaceId AuthRelation.FolderDelete

        return
            ({
                CreateResource = createResource
                EditResource = editResource
                DeleteResource = deleteResource
                CreateApp = createApp
                EditApp = editApp
                DeleteApp = deleteApp
                RunApp = runApp
                CreateDashboard = createDashboard
                EditDashboard = editDashboard
                DeleteDashboard = deleteDashboard
                RunDashboard = runDashboard
                CreateFolder = createFolder
                EditFolder = editFolder
                DeleteFolder = deleteFolder
            }
            : SpacePermissionsDto)
    }

    let private getAccessibleSpaces (ctx: HttpContext) = task {
        let! admin = isOrgAdmin ctx

        if admin then
            let! allSpaces = (spaces ctx).GetAllAsync 0 Int32.MaxValue

            return
                allSpaces
                |> List.map (fun space -> space.State)
                |> List.sortBy (fun space -> space.Name)
        else
            let actor = UiContext.actorUserId ctx
            let! memberSpaces = (spaces ctx).GetByUserIdAsync actor
            let! moderatorSpaces = (spaces ctx).GetByModeratorUserIdAsync actor

            return
                (memberSpaces @ moderatorSpaces)
                |> List.distinctBy (fun space -> space.State.Id)
                |> List.map (fun space -> space.State)
                |> List.sortBy (fun space -> space.Name)
    }

    let private getSpace (ctx: HttpContext) (spaceId: string) = task {
        match trySpaceId spaceId with
        | None -> return None
        | Some sid ->
            let! space = (spaces ctx).GetByIdAsync sid
            return space |> Option.map (fun space -> space.State)
    }

    let private requireSpace (ctx: HttpContext) (spaceId: string) (next: SpaceData -> Task<unit>) = task {
        let! space = getSpace ctx spaceId

        match space with
        | None ->
            return!
                writeStatus
                    ctx
                    StatusCodes.Status404NotFound
                    "Space not found"
                    "That space does not exist or has been deleted."
        | Some space ->
            let! accessible = getAccessibleSpaces ctx

            if accessible |> List.exists (fun item -> item.Id = space.Id) then
                return! next space
            else
                return!
                    writeStatus
                        ctx
                        StatusCodes.Status404NotFound
                        "Space not found"
                        "That space does not exist or has been deleted."
    }

    let private allUsers (ctx: HttpContext) = task {
        let! result = (users ctx).GetAllAsync 0 Int32.MaxValue

        return
            result
            |> List.map (fun user -> user.State)
            |> List.sortBy (fun user -> user.Email)
    }

    let private allFoldersForSpace (ctx: HttpContext) (space: SpaceData) = task {
        let! values = (folders ctx).GetBySpaceAsync space.Id 0 Int32.MaxValue

        return
            values
            |> List.map (fun folder -> folder.State)
            |> List.sortBy (fun folder -> folder.Name.Value)
    }

    let private allResourcesForSpace (ctx: HttpContext) (space: SpaceData) = task {
        let! values = (resources ctx).GetBySpaceAsync space.Id 0 Int32.MaxValue

        return
            values
            |> List.map (fun resource -> ResponseSanitizer.sanitizeResource resource.State)
            |> List.sortBy (fun resource -> resource.Name.Value)
    }

    let private getCurrentUser (ctx: HttpContext) = task {
        let actor = UiContext.actorUserId ctx
        let! user = (users ctx).GetByIdAsync actor

        match user with
        | None -> return Error(NotFound "User not found")
        | Some user ->
            return
                Ok {
                    Id = actor.Value.ToString()
                    Email = User.getEmail user
                    FirstName = User.getFirstName user
                    LastName = User.getLastName user
                }
    }

    let private getSpaceFromFolder (ctx: HttpContext) (folderId: FolderId) = task {
        let! folder = (folders ctx).GetByIdAsync folderId

        match folder with
        | None -> return None
        | Some folder ->
            let! space = (spaces ctx).GetByIdAsync folder.State.SpaceId
            return space |> Option.map (fun space -> space.State)
    }

    let private getSpaceFromFolderIncludingDeleted (ctx: HttpContext) (folderId: FolderId) = task {
        let! folder = (folders ctx).GetByIdAsync folderId

        match folder with
        | Some folder ->
            let! space = (spaces ctx).GetByIdAsync folder.State.SpaceId
            return space |> Option.map (fun space -> space.State)
        | None ->
            let! deletedFolder = (folders ctx).GetDeletedByIdAsync folderId

            match deletedFolder with
            | None -> return None
            | Some folder ->
                let! space = (spaces ctx).GetByIdAsync folder.State.SpaceId
                return space |> Option.map (fun space -> space.State)
    }

    let private getSpaceFromApp (ctx: HttpContext) (appId: AppId) = task {
        let! app = (apps ctx).GetByIdAsync appId

        match app with
        | None -> return None
        | Some app -> return! getSpaceFromFolder ctx app.State.FolderId
    }

    let private getSpaceFromDashboard (ctx: HttpContext) (dashboardId: DashboardId) = task {
        let! dashboard = (dashboards ctx).GetByIdAsync dashboardId

        match dashboard with
        | None -> return None
        | Some dashboard -> return! getSpaceFromFolder ctx dashboard.State.FolderId
    }

    let private createSpaceRelationships (ctx: HttpContext) (space: SpaceData) = task {
        let tuples =
            [
                {
                    Subject = AuthSubject.Organization "default"
                    Relation = AuthRelation.SpaceOrganization
                    Object = AuthObject.SpaceObject(space.Id.Value.ToString())
                }
                {
                    Subject = AuthSubject.User(space.ModeratorUserId.Value.ToString())
                    Relation = AuthRelation.SpaceModerator
                    Object = AuthObject.SpaceObject(space.Id.Value.ToString())
                }
            ]
            @ (space.MemberIds
               |> List.map (fun memberId -> {
                   Subject = AuthSubject.User(memberId.Value.ToString())
                   Relation = AuthRelation.SpaceMember
                   Object = AuthObject.SpaceObject(space.Id.Value.ToString())
               }))

        do! (auth ctx).CreateRelationshipsAsync tuples
    }

    let index: EndpointHandler = fun ctx -> redirect ctx "/spaces"

    let spacesPage: EndpointHandler =
        fun ctx -> task {
            let! accessible = getAccessibleSpaces ctx

            match accessible with
            | first :: _ -> return! redirect ctx $"/spaces/{first.Id.Value}"
            | [] ->
                let! userList = allUsers ctx
                let! admin = isOrgAdmin ctx
                return! writePage ctx "spaces" "Spaces" (Views.noSpaces (UiContext.antiforgeryToken ctx) admin userList)
        }

    let spacesListPage: EndpointHandler =
        fun ctx -> task {
            let! accessible = getAccessibleSpaces ctx
            let! userList = allUsers ctx
            let! admin = isOrgAdmin ctx

            return!
                writePage
                    ctx
                    "spaces-list"
                    "Spaces List"
                    (Views.spacesList (UiContext.antiforgeryToken ctx) accessible userList admin)
        }

    let spaceRootPage (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                let! folders = allFoldersForSpace ctx space
                let rootFolders = folders |> List.filter (fun folder -> folder.ParentId.IsNone)
                let! allApps = (apps ctx).GetBySpaceIdsAsync [ space.Id ] 0 Int32.MaxValue
                let! allDashboards = (dashboards ctx).GetBySpaceIdsAsync [ space.Id ] 0 Int32.MaxValue
                let rootFolderIds = rootFolders |> List.map (fun folder -> folder.Id) |> Set.ofList

                let rootApps =
                    allApps
                    |> List.map (fun app -> ResponseSanitizer.sanitizeApp app.State)
                    |> List.filter (fun app -> rootFolderIds |> Set.contains app.FolderId)
                    |> List.sortBy (fun app -> app.Name)

                let rootDashboards =
                    allDashboards
                    |> List.map (fun dashboard -> dashboard.State)
                    |> List.filter (fun dashboard -> rootFolderIds |> Set.contains dashboard.FolderId)
                    |> List.sortBy (fun dashboard -> dashboard.Name.Value)

                let! resources = allResourcesForSpace ctx space
                let! permissions = getSpaceUiPermissions ctx space.Id

                return!
                    writePage
                        ctx
                        "spaces"
                        space.Name
                        (Views.spaceHome
                            (UiContext.antiforgeryToken ctx)
                            space
                            rootFolders
                            rootApps
                            rootDashboards
                            resources
                            permissions)
            })

    let nodePage (spaceId: string) (nodeId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                let token = UiContext.antiforgeryToken ctx

                match Guid.TryParse nodeId with
                | false, _ -> return! writeStatus ctx StatusCodes.Status404NotFound "Not found" "Invalid node id."
                | true, guid ->
                    let fid = FolderId.FromGuid guid
                    let! folder = (folders ctx).GetByIdAsync fid

                    match folder with
                    | Some folder when folder.State.SpaceId = space.Id ->
                        let! allFolders = allFoldersForSpace ctx space
                        let childFolders = allFolders |> List.filter (fun item -> item.ParentId = Some fid)
                        let! folderApps = (apps ctx).GetByFolderIdAsync fid 0 Int32.MaxValue
                        let! folderDashboards = (dashboards ctx).GetByFolderIdAsync fid 0 Int32.MaxValue
                        let! folderResources = allResourcesForSpace ctx space
                        let! permissions = getSpaceUiPermissions ctx space.Id

                        return!
                            writePage
                                ctx
                                "spaces"
                                folder.State.Name.Value
                                (Views.folderPage
                                    token
                                    space
                                    folder.State
                                    childFolders
                                    (folderApps
                                     |> List.map (fun app -> ResponseSanitizer.sanitizeApp app.State)
                                     |> List.sortBy (fun app -> app.Name))
                                    (folderDashboards
                                     |> List.map (fun dashboard -> dashboard.State)
                                     |> List.sortBy (fun dashboard -> dashboard.Name.Value))
                                    folderResources
                                    permissions)
                    | Some _ ->
                        return!
                            writeStatus
                                ctx
                                StatusCodes.Status404NotFound
                                "Folder not found"
                                "That folder was not found in this space."
                    | None ->
                        let aid = AppId.FromGuid guid
                        let! app = (apps ctx).GetByIdAsync aid

                        match app with
                        | Some app ->
                            let! owningSpace = getSpaceFromFolder ctx app.State.FolderId

                            if owningSpace |> Option.exists (fun owning -> owning.Id = space.Id) then
                                let! resource = (resources ctx).GetByIdAsync app.State.ResourceId

                                match resource with
                                | Some resource ->
                                    return!
                                        writePage
                                            ctx
                                            "spaces"
                                            app.State.Name
                                            (Views.appEditor
                                                token
                                                space
                                                (ResponseSanitizer.sanitizeApp app.State)
                                                (ResponseSanitizer.sanitizeResource resource.State))
                                | None ->
                                    return!
                                        writeStatus
                                            ctx
                                            StatusCodes.Status404NotFound
                                            "Resource not found"
                                            "The app resource was not found."
                            else
                                return!
                                    writeStatus
                                        ctx
                                        StatusCodes.Status404NotFound
                                        "App not found"
                                        "That app was not found in this space."
                        | None ->
                            let did = DashboardId.FromGuid guid
                            let! dashboard = (dashboards ctx).GetByIdAsync did

                            match dashboard with
                            | Some dashboard ->
                                let! owningSpace = getSpaceFromFolder ctx dashboard.State.FolderId

                                if owningSpace |> Option.exists (fun owning -> owning.Id = space.Id) then
                                    let! allApps = (apps ctx).GetBySpaceIdsAsync [ space.Id ] 0 Int32.MaxValue

                                    return!
                                        writePage
                                            ctx
                                            "spaces"
                                            dashboard.State.Name.Value
                                            (Views.dashboardEditor
                                                token
                                                space
                                                dashboard.State
                                                (allApps
                                                 |> List.map (fun app -> ResponseSanitizer.sanitizeApp app.State)
                                                 |> List.sortBy (fun app -> app.Name)))
                                else
                                    return!
                                        writeStatus
                                            ctx
                                            StatusCodes.Status404NotFound
                                            "Dashboard not found"
                                            "That dashboard was not found in this space."
                            | None ->
                                return!
                                    writeStatus
                                        ctx
                                        StatusCodes.Status404NotFound
                                        "Not found"
                                        "That folder, app, or dashboard was not found."
            })

    let resourcesPage (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                let! resourceList = allResourcesForSpace ctx space
                let! appList = (apps ctx).GetBySpaceIdsAsync [ space.Id ] 0 Int32.MaxValue
                let! permissions = getSpaceUiPermissions ctx space.Id

                return!
                    writePage
                        ctx
                        "spaces"
                        $"{space.Name} Resources"
                        (Views.resourcesPage
                            (UiContext.antiforgeryToken ctx)
                            space
                            resourceList
                            (appList |> List.map (fun app -> ResponseSanitizer.sanitizeApp app.State))
                            permissions)
            })

    let settingsPage (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                let! userList = allUsers ctx

                let! permissionsResult =
                    (spaceHandler ctx)
                        .HandleCommand(GetSpaceMembersWithPermissions(space.Id.Value.ToString(), 0, UiModels.PageSize))

                let members =
                    match permissionsResult with
                    | Ok(SpaceMembersPermissionsResult result) -> result.Members.Items
                    | _ -> []

                let! defaultResult =
                    (spaceHandler ctx).HandleCommand(GetDefaultMemberPermissions(space.Id.Value.ToString()))

                let defaultPermissions =
                    match defaultResult with
                    | Ok(SpaceDefaultMemberPermissionsResult result) -> result.Permissions
                    | _ -> {
                        CreateResource = false
                        EditResource = false
                        DeleteResource = false
                        CreateApp = false
                        EditApp = false
                        DeleteApp = false
                        RunApp = false
                        CreateDashboard = false
                        EditDashboard = false
                        DeleteDashboard = false
                        RunDashboard = false
                        CreateFolder = false
                        EditFolder = false
                        DeleteFolder = false
                      }

                return!
                    writePage
                        ctx
                        "spaces"
                        $"{space.Name} Settings"
                        (Views.settingsPage (UiContext.antiforgeryToken ctx) space userList members defaultPermissions)
            })

    let permissionsAlias (spaceId: string) : EndpointHandler =
        fun ctx -> redirect ctx $"/spaces/{spaceId}/settings"

    let usersPage: EndpointHandler =
        fun ctx -> task {
            let! userList = allUsers ctx
            let! spaceList = getAccessibleSpaces ctx

            return!
                writePage ctx "users" "Users" (Views.usersPage userList spaceList ((UiContext.currentUser ctx).UserId))
        }

    let auditPage: EndpointHandler =
        fun ctx -> task {
            let page = queryPage ctx
            let skip = (page - 1) * UiModels.PageSize
            let scope = queryOption ctx "scope"
            let appId = queryValue ctx "appId"
            let userId = queryValue ctx "userId"
            let dashboardId = queryValue ctx "dashboardId"
            let search = queryOption ctx "q"
            let actorUserId = queryOption ctx "actorUserId"
            let eventType = queryOption ctx "eventType"
            let entityType = queryOption ctx "entityType"
            let fromDateText = queryOption ctx "fromDate"
            let toDateText = queryOption ctx "toDate"

            let includeRunEvents =
                queryOption ctx "includeRunEvents"
                |> Option.map (parseBool true)
                |> Option.defaultValue true

            let parseActorUserId =
                match actorUserId with
                | None -> Ok None
                | Some value ->
                    match Guid.TryParse value with
                    | true, guid -> Ok(Some(UserId.FromGuid guid))
                    | _ -> Error "Actor user must be a valid user id."

            let parseEventType =
                match eventType with
                | None -> Ok None
                | Some value ->
                    match EventTypeConverter.fromString value with
                    | Some parsed -> Ok(Some parsed)
                    | None -> Error $"Unsupported event type: {value}."

            let parseEntityType =
                match entityType with
                | None -> Ok None
                | Some value ->
                    match EntityTypeConverter.fromString value with
                    | Some parsed -> Ok(Some parsed)
                    | None -> Error $"Unsupported entity type: {value}."

            let fromDateResult = parseDateQuery "From date" false fromDateText
            let toDateResult = parseDateQuery "To date" true toDateText

            match parseActorUserId, parseEventType, parseEntityType, fromDateResult, toDateResult with
            | Error message, _, _, _, _
            | _, Error message, _, _, _
            | _, _, Error message, _, _
            | _, _, _, Error message, _
            | _, _, _, _, Error message ->
                return! writeStatus ctx StatusCodes.Status400BadRequest "Invalid audit query" message
            | Ok actorUserIdFilter, Ok eventTypeFilter, Ok entityTypeFilter, Ok fromDate, Ok toDate ->
                match fromDate, toDate with
                | Some fromDateValue, Some toDateValue when fromDateValue > toDateValue ->
                    return!
                        writeStatus
                            ctx
                            StatusCodes.Status400BadRequest
                            "Invalid audit query"
                            "From date must be on or before to date."
                | _ ->
                    let needsInMemoryFiltering =
                        search.IsSome
                        || (scope.IsSome
                            && (actorUserIdFilter.IsSome || eventTypeFilter.IsSome || entityTypeFilter.IsSome))

                    let querySkip = if needsInMemoryFiltering then 0 else skip

                    let queryTake =
                        if needsInMemoryFiltering then
                            Int32.MaxValue
                        else
                            UiModels.PageSize

                    let! eventsResult = task {
                        match scope with
                        | Some "app" ->
                            match Guid.TryParse appId with
                            | true, guid ->
                                let! result =
                                    events ctx
                                    |> fun repo ->
                                        repo.GetEventsByAppIdAsync(
                                            {
                                                AppId = AppId.FromGuid guid
                                                FromDate = fromDate
                                                ToDate = toDate
                                                Skip = querySkip
                                                Take = queryTake
                                                IncludeRunEvents = includeRunEvents
                                            }
                                        )

                                return Ok result
                            | _ -> return Error "Invalid or missing appId query parameter."
                        | Some "user" ->
                            match Guid.TryParse userId with
                            | true, guid ->
                                let! result =
                                    events ctx
                                    |> fun repo ->
                                        repo.GetEventsByUserIdAsync(
                                            {
                                                UserId = UserId.FromGuid guid
                                                FromDate = fromDate
                                                ToDate = toDate
                                                Skip = querySkip
                                                Take = queryTake
                                            }
                                        )

                                return Ok result
                            | _ -> return Error "Invalid or missing userId query parameter."
                        | Some "dashboard" ->
                            match Guid.TryParse dashboardId with
                            | true, guid ->
                                let! result =
                                    events ctx
                                    |> fun repo ->
                                        repo.GetEventsByDashboardIdAsync(
                                            {
                                                DashboardId = DashboardId.FromGuid guid
                                                FromDate = fromDate
                                                ToDate = toDate
                                                Skip = querySkip
                                                Take = queryTake
                                            }
                                        )

                                return Ok result
                            | _ -> return Error "Invalid or missing dashboardId query parameter."
                        | Some other -> return Error $"Unsupported audit scope: {other}."
                        | None ->
                            let! result =
                                events ctx
                                |> fun repo ->
                                    repo.GetEventsAsync(
                                        {
                                            UserId = actorUserIdFilter
                                            EventType = eventTypeFilter
                                            EntityType = entityTypeFilter
                                            FromDate = fromDate
                                            ToDate = toDate
                                            Skip = querySkip
                                            Take = queryTake
                                        }
                                    )

                            return Ok result
                    }

                    match eventsResult with
                    | Error message ->
                        return! writeStatus ctx StatusCodes.Status400BadRequest "Invalid audit query" message
                    | Ok result ->
                        let rawMatches (event: EventData) =
                            let actorMatches =
                                match actorUserIdFilter with
                                | Some userId -> event.UserId = userId
                                | None -> true

                            let eventTypeMatches =
                                match eventTypeFilter with
                                | Some selected -> event.EventType = selected
                                | None -> true

                            let entityTypeMatches =
                                match entityTypeFilter with
                                | Some selected -> event.EntityType = selected
                                | None -> true

                            actorMatches && eventTypeMatches && entityTypeMatches

                        let rawEvents =
                            if needsInMemoryFiltering then
                                result.Items |> List.filter rawMatches
                            else
                                result.Items

                        let enhancementTasks =
                            rawEvents
                            |> List.map (fun event -> (eventEnhancement ctx).EnhanceEventAsync event)
                            |> List.toArray

                        let! enhancedEvents = Task.WhenAll enhancementTasks

                        let containsText (needle: string) (value: string) =
                            not (String.IsNullOrWhiteSpace value)
                            && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0

                        let matchesSearch (event: EnhancedEventData) =
                            match search with
                            | None -> true
                            | Some query ->
                                [
                                    event.EventSummary
                                    event.EntityName
                                    event.EntityId
                                    event.EventId
                                    event.UserName
                                    event.UserId.Value.ToString()
                                    UiFormat.eventType event.EventType
                                    UiFormat.entityType event.EntityType
                                    event.EventData
                                ]
                                |> List.exists (containsText query)

                        let filteredEvents = enhancedEvents |> Array.toList |> List.filter matchesSearch

                        let visibleEvents, totalCount =
                            if needsInMemoryFiltering then
                                filteredEvents |> List.skip skip |> List.truncate UiModels.PageSize,
                                filteredEvents.Length
                            else
                                filteredEvents, result.TotalCount

                        let! scopeText = task {
                            match scope with
                            | Some "user" ->
                                match Guid.TryParse userId with
                                | true, guid ->
                                    let userId = UserId.FromGuid guid
                                    let! user = (users ctx).GetByIdAsync userId

                                    return
                                        user
                                        |> Option.map (fun user ->
                                            $"Showing audit events for {user.State.Name} ({user.State.Email}).")
                                        |> Option.orElse (Some $"Showing audit events for user {userId.Value}.")
                                | _ -> return Some "Showing audit events for the selected user."
                            | Some "app" ->
                                match Guid.TryParse appId with
                                | true, guid ->
                                    let appId = AppId.FromGuid guid
                                    let! app = (apps ctx).GetByIdAsync appId

                                    return
                                        app
                                        |> Option.map (fun app -> $"Showing audit events for app {app.State.Name}.")
                                        |> Option.orElse (Some $"Showing audit events for app {appId.Value}.")
                                | _ -> return Some "Showing audit events for the selected app."
                            | Some "dashboard" ->
                                match Guid.TryParse dashboardId with
                                | true, guid ->
                                    let dashboardId = DashboardId.FromGuid guid
                                    let! dashboard = (dashboards ctx).GetByIdAsync dashboardId

                                    return
                                        dashboard
                                        |> Option.map (fun dashboard ->
                                            $"Showing audit events for dashboard {dashboard.State.Name.Value}.")
                                        |> Option.orElse (
                                            Some $"Showing audit events for dashboard {dashboardId.Value}."
                                        )
                                | _ -> return Some "Showing audit events for the selected dashboard."
                            | Some other -> return Some $"Showing audit events for scope {other}."
                            | None -> return None
                        }

                        let includeRunEventsParam =
                            if scope = Some "app" && not includeRunEvents then
                                Some "false"
                            else
                                None

                        let baseHref =
                            buildQueryHref "/audit" [
                                "scope", scope
                                "appId", optionalText appId
                                "userId", optionalText userId
                                "dashboardId", optionalText dashboardId
                                "q", search
                                "actorUserId", actorUserId
                                "eventType", eventType
                                "entityType", entityType
                                "fromDate", fromDateText
                                "toDate", toDateText
                                "includeRunEvents", includeRunEventsParam
                            ]

                        let filterValues: Views.AuditFilterValues = {
                            Scope = scope
                            AppId = optionalText appId
                            UserId = optionalText userId
                            DashboardId = optionalText dashboardId
                            Search = search
                            ActorUserId = actorUserId
                            EventType = eventType
                            EntityType = entityType
                            FromDate = fromDateText
                            ToDate = toDateText
                            IncludeRunEvents = includeRunEvents
                        }

                        let! userList = allUsers ctx

                        return!
                            writePage
                                ctx
                                "audit"
                                "Audit"
                                (Views.auditPage userList visibleEvents page totalCount scopeText baseHref filterValues)
        }

    let trashPage (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                let! deletedApps = (folders ctx).GetBySpaceAsync space.Id 0 Int32.MaxValue
                let folderIds = deletedApps |> List.map (fun folder -> folder.State.Id)
                let! trashApps = (apps ctx).GetDeletedByFolderIdsAsync folderIds
                let! trashFolders = (folders ctx).GetDeletedBySpaceAsync space.Id
                let! trashResources = (resources ctx).GetDeletedBySpaceAsync space.Id

                return!
                    writePage
                        ctx
                        "spaces"
                        $"{space.Name} Trash"
                        (Views.trashPage (UiContext.antiforgeryToken ctx) space trashApps trashFolders trashResources)
            })

    let runAppPage (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                match tryAppId appId with
                | None -> return! writeStatus ctx StatusCodes.Status404NotFound "App not found" "Invalid app id."
                | Some aid ->
                    let! app = (apps ctx).GetByIdAsync aid

                    match app with
                    | None ->
                        return! writeStatus ctx StatusCodes.Status404NotFound "App not found" "That app was not found."
                    | Some app ->
                        let! owningSpace = getSpaceFromFolder ctx app.State.FolderId

                        if owningSpace |> Option.exists (fun owning -> owning.Id = space.Id) then
                            return!
                                writePage
                                    ctx
                                    "spaces"
                                    $"Run {app.State.Name}"
                                    (Views.runAppPage
                                        (UiContext.antiforgeryToken ctx)
                                        space
                                        (ResponseSanitizer.sanitizeApp app.State)
                                        None)
                        else
                            return!
                                writeStatus
                                    ctx
                                    StatusCodes.Status404NotFound
                                    "App not found"
                                    "That app was not found in this space."
            })

    let dashboardRunPage (spaceId: string) (dashboardId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                match tryDashboardId dashboardId with
                | None ->
                    return! writeStatus ctx StatusCodes.Status404NotFound "Dashboard not found" "Invalid dashboard id."
                | Some did ->
                    let! dashboard = (dashboards ctx).GetByIdAsync did

                    match dashboard with
                    | None ->
                        return!
                            writeStatus
                                ctx
                                StatusCodes.Status404NotFound
                                "Dashboard not found"
                                "That dashboard was not found."
                    | Some dashboard ->
                        let! owningSpace = getSpaceFromFolder ctx dashboard.State.FolderId

                        if owningSpace |> Option.exists (fun owning -> owning.Id = space.Id) then
                            return!
                                writePage
                                    ctx
                                    "spaces"
                                    $"Run {dashboard.State.Name.Value}"
                                    (Views.dashboardRuntimePage
                                        (UiContext.antiforgeryToken ctx)
                                        space
                                        dashboard.State
                                        None)
                        else
                            return!
                                writeStatus
                                    ctx
                                    StatusCodes.Status404NotFound
                                    "Dashboard not found"
                                    "That dashboard was not found in this space."
            })

    let devSelectUserPage: EndpointHandler =
        fun ctx -> task {
            let returnUrl =
                queryValue ctx "returnUrl" |> optionalText |> Option.defaultValue "/spaces"

            let! userList = allUsers ctx
            let! model = UiContext.layoutModel ctx "" "Choose development user" None

            return!
                ctx.WriteHtmlView(
                    LayoutView.page model (Views.devSelectUser (UiContext.antiforgeryToken ctx) userList returnUrl None)
                )
        }

    let devSelectUserPost: EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx
            let userId = formValue posted "userId"

            let returnUrl =
                optionalFormValue posted "returnUrl" |> Option.defaultValue "/spaces"

            match trySpaceId userId with
            | None -> return! redirectWithFlash ctx "/dev/select-user" "error" "Invalid user id."
            | Some _ ->
                match Guid.TryParse userId with
                | false, _ -> return! redirectWithFlash ctx "/dev/select-user" "error" "Invalid user id."
                | true, guid ->
                    let! user = (users ctx).GetByIdAsync(UserId.FromGuid guid)

                    match user with
                    | None -> return! redirectWithFlash ctx "/dev/select-user" "error" "User not found."
                    | Some _ ->
                        ctx.Response.Cookies.Append(
                            UiModels.DevUserCookieName,
                            userId,
                            CookieOptions(HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps)
                        )

                        return! redirect ctx returnUrl
        }

    let createSpace: EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx
            let! admin = isOrgAdmin ctx

            if not admin then
                return!
                    redirectWithFlash ctx "/spaces-list" "error" "Only organization administrators can create spaces."
            else
                let memberIds = formValues posted "MemberIds"
                let inviteEmail = optionalFormValue posted "InviteEmail"
                let mutable effectiveMembers = memberIds

                match inviteEmail with
                | Some email ->
                    let userHandler = service<ICommandHandler<UserCommand, UserCommandResult>> ctx

                    let! inviteResult =
                        userHandler.HandleCommand(InviteUser(UiContext.actorUserId ctx, { Email = email }))

                    match inviteResult with
                    | Ok(UserResult user) -> effectiveMembers <- user.Id.Value.ToString() :: effectiveMembers
                    | _ -> ()
                | None -> ()

                let dto: CreateSpaceDto = {
                    Name = formValue posted "Name"
                    ModeratorUserId = formValue posted "ModeratorUserId"
                    MemberIds =
                        if List.isEmpty effectiveMembers then
                            None
                        else
                            Some(effectiveMembers |> List.distinct)
                }

                let! result = (spaceHandler ctx).HandleCommand(CreateSpace(UiContext.actorUserId ctx, dto))

                match result with
                | Ok(SpaceResult space) ->
                    do! createSpaceRelationships ctx space
                    return! redirectWithFlash ctx $"/spaces/{space.Id.Value}" "success" $"Created space {space.Name}."
                | Ok _ -> return! redirectWithFlash ctx "/spaces-list" "error" "Unexpected space command result."
                | Error error -> return! redirectWithFlash ctx "/spaces-list" "error" (UiFormat.domainError error)
        }

    let updateSpaceName (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! allowed = isOrgAdminOrModerator ctx space

                if not allowed then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only admins or moderators can rename spaces."
                else
                    let! result =
                        (spaceHandler ctx)
                            .HandleCommand(
                                UpdateSpaceName(UiContext.actorUserId ctx, spaceId, { Name = formValue posted "Name" })
                            )

                    match result with
                    | Ok _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}/settings" "success" "Space renamed."
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" (UiFormat.domainError error)
            })

    let changeModerator (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! admin = isOrgAdmin ctx

                if not admin then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only organization administrators can change moderators."
                else
                    let oldModerator = space.ModeratorUserId.Value.ToString()
                    let newModerator = formValue posted "NewModeratorUserId"
                    let dto = { NewModeratorUserId = newModerator }

                    let! result =
                        (spaceHandler ctx).HandleCommand(ChangeModerator(UiContext.actorUserId ctx, spaceId, dto))

                    match result with
                    | Ok(SpaceResult updated) ->
                        if oldModerator <> newModerator then
                            do!
                                (auth ctx)
                                    .UpdateRelationshipsAsync(
                                        {
                                            TuplesToAdd = [
                                                {
                                                    Subject = AuthSubject.User newModerator
                                                    Relation = AuthRelation.SpaceModerator
                                                    Object = AuthObject.SpaceObject spaceId
                                                }
                                            ]
                                            TuplesToRemove = [
                                                {
                                                    Subject = AuthSubject.User oldModerator
                                                    Relation = AuthRelation.SpaceModerator
                                                    Object = AuthObject.SpaceObject spaceId
                                                }
                                            ]
                                        }
                                    )

                        return! redirectWithFlash ctx $"/spaces/{spaceId}/settings" "success" "Moderator changed."
                    | Ok _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" "Unexpected result."
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" (UiFormat.domainError error)
            })

    let addMember (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! allowed = isOrgAdminOrModerator ctx space

                if not allowed then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only admins or moderators can add members."
                else
                    let memberId = formValue posted "UserId"

                    let! result =
                        (spaceHandler ctx)
                            .HandleCommand(AddMember(UiContext.actorUserId ctx, spaceId, { UserId = memberId }))

                    match result with
                    | Ok _ ->
                        do!
                            (auth ctx)
                                .CreateRelationshipsAsync(
                                    [
                                        {
                                            Subject = AuthSubject.User memberId
                                            Relation = AuthRelation.SpaceMember
                                            Object = AuthObject.SpaceObject spaceId
                                        }
                                    ]
                                )

                        return! redirectWithFlash ctx $"/spaces/{spaceId}/settings" "success" "Member added."
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" (UiFormat.domainError error)
            })

    let removeMember (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let memberId = formValue posted "UserId"
                let! allowed = isOrgAdminOrModerator ctx space

                if not allowed then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only admins or moderators can remove members."
                else
                    let! result =
                        (spaceHandler ctx)
                            .HandleCommand(RemoveMember(UiContext.actorUserId ctx, spaceId, { UserId = memberId }))

                    match result with
                    | Ok _ ->
                        do!
                            (auth ctx)
                                .DeleteRelationshipsAsync(
                                    [
                                        {
                                            Subject = AuthSubject.User memberId
                                            Relation = AuthRelation.SpaceMember
                                            Object = AuthObject.SpaceObject spaceId
                                        }
                                    ]
                                )

                        return! redirectWithFlash ctx $"/spaces/{spaceId}/settings" "success" "Member removed."
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" (UiFormat.domainError error)
            })

    let updateMemberPermissions (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! allowed = isOrgAdminOrModerator ctx space

                if not allowed then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only admins or moderators can update permissions."
                else
                    let updates =
                        match formValues posted "UserIds" |> List.distinct with
                        | [] ->
                            let userId = formValue posted "UserId"

                            if String.IsNullOrWhiteSpace userId then
                                []
                            else
                                [ userId, permissionsFromForm posted ]
                        | userIds ->
                            userIds
                            |> List.map (fun userId -> userId, permissionsForUserFromForm posted userId)

                    if List.isEmpty updates then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/settings"
                                "error"
                                "No member permissions were submitted."
                    else
                        let mutable errors = []

                        for userId, permissions in updates do
                            let! result =
                                (spaceHandler ctx)
                                    .HandleCommand(
                                        UpdateUserPermissions(
                                            UiContext.actorUserId ctx,
                                            spaceId,
                                            {
                                                UserId = userId
                                                Permissions = permissions
                                            }
                                        )
                                    )

                            match result with
                            | Ok _ -> ()
                            | Error error -> errors <- UiFormat.domainError error :: errors

                        match errors |> List.rev with
                        | [] ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/settings"
                                    "success"
                                    "Member permissions updated."
                        | firstError :: _ ->
                            return! redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" firstError
            })

    let updateDefaultMemberPermissions (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! allowed = isOrgAdminOrModerator ctx space

                if not allowed then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only admins or moderators can update default permissions."
                else
                    let permissions = permissionsFromForm posted

                    let! result =
                        (spaceHandler ctx)
                            .HandleCommand(
                                UpdateDefaultMemberPermissions(
                                    UiContext.actorUserId ctx,
                                    spaceId,
                                    { Permissions = permissions }
                                )
                            )

                    match result with
                    | Ok _ ->
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/settings"
                                "success"
                                "Default member permissions updated."
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" (UiFormat.domainError error)
            })

    let deleteSpace (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! admin = isOrgAdmin ctx

                if not admin then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            "Only organization administrators can delete spaces."
                elif formValue posted "ConfirmName" <> space.Name then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/settings"
                            "error"
                            $"Type {space.Name} to confirm deleting this space."
                else
                    let! result = (spaceHandler ctx).HandleCommand(DeleteSpace(UiContext.actorUserId ctx, spaceId))

                    match result with
                    | Ok _ ->
                        let tuples =
                            [
                                {
                                    Subject = AuthSubject.Organization "default"
                                    Relation = AuthRelation.SpaceOrganization
                                    Object = AuthObject.SpaceObject spaceId
                                }
                                {
                                    Subject = AuthSubject.User(space.ModeratorUserId.Value.ToString())
                                    Relation = AuthRelation.SpaceModerator
                                    Object = AuthObject.SpaceObject spaceId
                                }
                            ]
                            @ (space.MemberIds
                               |> List.map (fun id -> {
                                   Subject = AuthSubject.User(id.Value.ToString())
                                   Relation = AuthRelation.SpaceMember
                                   Object = AuthObject.SpaceObject spaceId
                               }))

                        do! (auth ctx).DeleteRelationshipsAsync tuples
                        return! redirectWithFlash ctx "/spaces-list" "success" "Space deleted."
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/settings" "error" (UiFormat.domainError error)
            })

    let createFolder (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let parentId = optionalFormValue posted "ParentId"
                let location = parentId |> Option.map ChildFolder |> Option.orElse (Some RootFolder)

                let dto = {
                    Name = formValue posted "Name"
                    Location = location
                    SpaceId = spaceId
                }

                let! allowed = hasSpacePermission ctx space.Id AuthRelation.FolderCreate

                if not allowed then
                    return! redirectBackToSpace ctx spaceId "error" "You do not have permission to create folders."
                else
                    match FolderMapper.fromCreateDto (UiContext.actorUserId ctx) dto with
                    | Error error -> return! redirectBackToSpace ctx spaceId "error" (UiFormat.domainError error)
                    | Ok folder ->
                        let! result =
                            (folderHandler ctx).HandleCommand(CreateFolder(UiContext.actorUserId ctx, folder))

                        match result with
                        | Ok(FolderResult created) ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{created.Id.Value}"
                                    "success"
                                    "Folder created."
                        | Error error -> return! redirectBackToSpace ctx spaceId "error" (UiFormat.domainError error)
                        | _ -> return! redirectBackToSpace ctx spaceId "error" "Unexpected folder result."
            })

    let renameFolder (spaceId: string) (folderId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryFolderId folderId with
            | None -> return! redirectBackToSpace ctx spaceId "error" "Invalid folder id."
            | Some fid ->
                let! space = getSpaceFromFolder ctx fid

                match space with
                | None -> return! redirectBackToSpace ctx spaceId "error" "Folder not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.FolderEdit

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{folderId}"
                                "error"
                                "You do not have permission to edit folders."
                    else
                        let! result =
                            (folderHandler ctx)
                                .HandleCommand(
                                    UpdateFolderName(
                                        UiContext.actorUserId ctx,
                                        folderId,
                                        { Name = formValue posted "Name" }
                                    )
                                )

                        match result with
                        | Ok _ ->
                            return! redirectWithFlash ctx $"/spaces/{spaceId}/{folderId}" "success" "Folder renamed."
                        | Error error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{folderId}"
                                    "error"
                                    (UiFormat.domainError error)
        }

    let deleteFolder (spaceId: string) (folderId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryFolderId folderId with
            | None -> return! redirectBackToSpace ctx spaceId "error" "Invalid folder id."
            | Some fid ->
                let! folder = (folders ctx).GetByIdAsync fid

                match folder with
                | None -> return! redirectBackToSpace ctx spaceId "error" "Folder not found."
                | Some folder when folder.State.SpaceId.Value.ToString() <> spaceId ->
                    return! redirectBackToSpace ctx spaceId "error" "Folder not found in this space."
                | Some folder ->
                    let target =
                        match folder.State.ParentId with
                        | Some parentId -> $"/spaces/{spaceId}/{parentId.Value}"
                        | None -> $"/spaces/{spaceId}"

                    let! allowed = hasSpacePermission ctx folder.State.SpaceId AuthRelation.FolderDelete

                    if not allowed then
                        return! redirectWithFlash ctx target "error" "You do not have permission to delete folders."
                    else
                        let! result =
                            (folderHandler ctx).HandleCommand(DeleteFolder(UiContext.actorUserId ctx, folderId))

                        match result with
                        | Ok(FolderUnitResult _) -> return! redirectWithFlash ctx target "success" "Folder deleted."
                        | Error error -> return! redirectWithFlash ctx target "error" (UiFormat.domainError error)
                        | _ -> return! redirectWithFlash ctx target "error" "Unexpected folder result."
        }

    let deleteApp (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryAppId appId with
            | None -> return! redirectBackToSpace ctx spaceId "error" "Invalid app id."
            | Some aid ->
                let! app = (apps ctx).GetByIdAsync aid

                match app with
                | None -> return! redirectBackToSpace ctx spaceId "error" "App not found."
                | Some app ->
                    let target = $"/spaces/{spaceId}/{app.State.FolderId.Value}"
                    let! owningSpace = getSpaceFromFolder ctx app.State.FolderId

                    match owningSpace with
                    | None -> return! redirectBackToSpace ctx spaceId "error" "App folder not found."
                    | Some owningSpace when owningSpace.Id.Value.ToString() <> spaceId ->
                        return! redirectBackToSpace ctx spaceId "error" "App not found in this space."
                    | Some owningSpace ->
                        let! allowed = hasSpacePermission ctx owningSpace.Id AuthRelation.AppDelete

                        if not allowed then
                            return! redirectWithFlash ctx target "error" "You do not have permission to delete apps."
                        else
                            let! result = (appHandler ctx).HandleCommand(DeleteApp(UiContext.actorUserId ctx, appId))

                            match result with
                            | Ok(AppUnitResult _) -> return! redirectWithFlash ctx target "success" "App deleted."
                            | Error error -> return! redirectWithFlash ctx target "error" (UiFormat.domainError error)
                            | _ -> return! redirectWithFlash ctx target "error" "Unexpected app result."
        }

    let deleteDashboard (spaceId: string) (dashboardId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryDashboardId dashboardId with
            | None -> return! redirectBackToSpace ctx spaceId "error" "Invalid dashboard id."
            | Some did ->
                let! dashboard = (dashboards ctx).GetByIdAsync did

                match dashboard with
                | None -> return! redirectBackToSpace ctx spaceId "error" "Dashboard not found."
                | Some dashboard ->
                    let target = $"/spaces/{spaceId}/{dashboard.State.FolderId.Value}"
                    let! owningSpace = getSpaceFromFolder ctx dashboard.State.FolderId

                    match owningSpace with
                    | None -> return! redirectBackToSpace ctx spaceId "error" "Dashboard folder not found."
                    | Some owningSpace when owningSpace.Id.Value.ToString() <> spaceId ->
                        return! redirectBackToSpace ctx spaceId "error" "Dashboard not found in this space."
                    | Some owningSpace ->
                        let! allowed = hasSpacePermission ctx owningSpace.Id AuthRelation.DashboardDelete

                        if not allowed then
                            return!
                                redirectWithFlash ctx target "error" "You do not have permission to delete dashboards."
                        else
                            let! result =
                                (dashboardHandler ctx)
                                    .HandleCommand(DeleteDashboard(UiContext.actorUserId ctx, dashboardId))

                            match result with
                            | Ok(DashboardUnitResult()) ->
                                return! redirectWithFlash ctx target "success" "Dashboard deleted."
                            | Error error -> return! redirectWithFlash ctx target "error" (UiFormat.domainError error)
                            | _ -> return! redirectWithFlash ctx target "error" "Unexpected dashboard result."
        }

    let createResource (spaceId: string) : EndpointHandler =
        fun ctx ->
            requireSpace ctx spaceId (fun space -> task {
                do! validatePost ctx
                let! posted = form ctx
                let! allowed = hasSpacePermission ctx space.Id AuthRelation.ResourceCreate

                if not allowed then
                    return!
                        redirectWithFlash
                            ctx
                            $"/spaces/{spaceId}/resources"
                            "error"
                            "You do not have permission to create resources."
                else
                    let kind = formValue posted "Kind"

                    let result =
                        if kind.Equals("sql", StringComparison.OrdinalIgnoreCase) then
                            ResourceMapper.fromCreateSqlDto (UiContext.actorUserId ctx) {
                                Name = formValue posted "Name"
                                Description = formValue posted "Description"
                                SpaceId = spaceId
                                DatabaseName = formValue posted "DatabaseName"
                                DatabaseHost = formValue posted "DatabaseHost"
                                DatabasePort = formValue posted "DatabasePort" |> parsePositiveInt 5432
                                DatabaseEngine =
                                    optionalFormValue posted "DatabaseEngine" |> Option.defaultValue "POSTGRES"
                                DatabaseAuthScheme =
                                    optionalFormValue posted "DatabaseAuthScheme"
                                    |> Option.defaultValue "USERNAME_PASSWORD"
                                DatabaseUsername = formValue posted "DatabaseUsername"
                                DatabasePassword = optionalFormValue posted "DatabasePassword"
                                UseSsl = formValues posted "UseSsl" |> List.contains "true"
                                EnableSshTunnel = formValues posted "EnableSshTunnel" |> List.contains "true"
                                ConnectionOptions = keyValuePairs posted "ConnectionOption"
                            }
                        else
                            ResourceMapper.fromCreateHttpDto (UiContext.actorUserId ctx) {
                                Name = formValue posted "Name"
                                Description = formValue posted "Description"
                                SpaceId = spaceId
                                BaseUrl = formValue posted "BaseUrl"
                                UrlParameters = keyValuePairs posted "UrlParameter"
                                Headers = keyValuePairs posted "Header"
                                Body = keyValuePairs posted "Body"
                            }

                    match result with
                    | Error error ->
                        return!
                            redirectWithFlash ctx $"/spaces/{spaceId}/resources" "error" (UiFormat.domainError error)
                    | Ok resource ->
                        let! saved =
                            (resourceHandler ctx).HandleCommand(CreateResource(UiContext.actorUserId ctx, resource))

                        match saved with
                        | Ok _ ->
                            return! redirectWithFlash ctx $"/spaces/{spaceId}/resources" "success" "Resource created."
                        | Error error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/resources"
                                    "error"
                                    (UiFormat.domainError error)
            })

    let updateResource (spaceId: string) (resourceId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx
            let target = $"/spaces/{spaceId}/resources"

            match tryResourceId resourceId with
            | None -> return! redirectWithFlash ctx target "error" "Invalid resource id."
            | Some rid ->
                let! resource = (resources ctx).GetByIdAsync rid

                match resource with
                | None -> return! redirectWithFlash ctx target "error" "Resource not found."
                | Some resource when resource.State.SpaceId.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx target "error" "Resource not found in this space."
                | Some resource ->
                    let! allowed = hasSpacePermission ctx resource.State.SpaceId AuthRelation.ResourceEdit

                    if not allowed then
                        return! redirectWithFlash ctx target "error" "You do not have permission to edit resources."
                    else
                        let actor = UiContext.actorUserId ctx
                        let handler = resourceHandler ctx
                        let mutable changed = false
                        let mutable firstError: DomainError option = None

                        let keyValueTuples (pairs: KeyValuePair list) =
                            pairs |> List.map (fun pair -> pair.Key, pair.Value)

                        let dtoTuples (pairs: KeyValuePairDto list) =
                            pairs |> List.map (fun pair -> pair.Key, pair.Value)

                        let applyIfChanged hasChanged command = task {
                            if hasChanged && firstError.IsNone then
                                changed <- true
                                let! result = handler.HandleCommand command

                                match result with
                                | Error error -> firstError <- Some error
                                | Ok _ -> ()
                        }

                        let submittedName = formValue posted "Name"
                        let submittedDescription = formValue posted "Description"

                        do!
                            applyIfChanged
                                (submittedName <> resource.State.Name.Value)
                                (UpdateResourceName(actor, resourceId, { Name = submittedName }))

                        do!
                            applyIfChanged
                                (submittedDescription <> resource.State.Description.Value)
                                (UpdateResourceDescription(actor, resourceId, { Description = submittedDescription }))

                        match resource.State.ResourceKind with
                        | ResourceKind.Http ->
                            let submittedBaseUrl = formValue posted "BaseUrl"

                            let currentBaseUrl =
                                resource.State.BaseUrl |> Option.map string |> Option.defaultValue String.Empty

                            let submittedUrlParameters = keyValuePairs posted "UrlParameter"
                            let submittedHeaders = keyValuePairs posted "Header"
                            let submittedBody = keyValuePairs posted "Body"

                            let displayedHeaderTuples =
                                resource.State.Headers
                                |> keyValueTuples
                                |> AuthorizationHeaderRedaction.redactTupleHeaders

                            do!
                                applyIfChanged
                                    (submittedBaseUrl <> currentBaseUrl)
                                    (UpdateResourceBaseUrl(actor, resourceId, { BaseUrl = submittedBaseUrl }))

                            do!
                                applyIfChanged
                                    (dtoTuples submittedUrlParameters <> keyValueTuples resource.State.UrlParameters)
                                    (UpdateResourceUrlParameters(
                                        actor,
                                        resourceId,
                                        {
                                            UrlParameters = submittedUrlParameters
                                        }
                                    ))

                            do!
                                applyIfChanged
                                    (dtoTuples submittedHeaders <> displayedHeaderTuples)
                                    (UpdateResourceHeaders(actor, resourceId, { Headers = submittedHeaders }))

                            do!
                                applyIfChanged
                                    (dtoTuples submittedBody <> keyValueTuples resource.State.Body)
                                    (UpdateResourceBody(actor, resourceId, { Body = submittedBody }))
                        | ResourceKind.Sql ->
                            let submittedDatabaseName = formValue posted "DatabaseName"
                            let submittedDatabaseHost = formValue posted "DatabaseHost"
                            let submittedDatabasePort = formValue posted "DatabasePort" |> parsePositiveInt 5432

                            let submittedDatabaseEngine =
                                optionalFormValue posted "DatabaseEngine" |> Option.defaultValue "POSTGRES"

                            let submittedDatabaseAuthScheme =
                                optionalFormValue posted "DatabaseAuthScheme"
                                |> Option.defaultValue "USERNAME_PASSWORD"

                            let submittedDatabaseUsername = formValue posted "DatabaseUsername"
                            let submittedDatabasePassword = optionalFormValue posted "DatabasePassword"
                            let submittedUseSsl = formValues posted "UseSsl" |> List.contains "true"

                            let submittedEnableSshTunnel =
                                formValues posted "EnableSshTunnel" |> List.contains "true"

                            let submittedConnectionOptions = keyValuePairs posted "ConnectionOption"

                            let currentDatabaseName =
                                resource.State.DatabaseName
                                |> Option.map string
                                |> Option.defaultValue String.Empty

                            let currentDatabaseHost =
                                resource.State.DatabaseHost
                                |> Option.map string
                                |> Option.defaultValue String.Empty

                            let currentDatabasePort =
                                resource.State.DatabasePort
                                |> Option.map (fun value -> value.Value)
                                |> Option.defaultValue 5432

                            let currentDatabaseEngine =
                                resource.State.DatabaseEngine
                                |> Option.map string
                                |> Option.defaultValue "POSTGRES"

                            let currentDatabaseAuthScheme =
                                resource.State.DatabaseAuthScheme
                                |> Option.map string
                                |> Option.defaultValue "USERNAME_PASSWORD"

                            let currentDatabaseUsername =
                                resource.State.DatabaseUsername
                                |> Option.map string
                                |> Option.defaultValue String.Empty

                            let databaseChanged =
                                submittedDatabaseName <> currentDatabaseName
                                || submittedDatabaseHost <> currentDatabaseHost
                                || submittedDatabasePort <> currentDatabasePort
                                || submittedDatabaseEngine <> currentDatabaseEngine
                                || submittedDatabaseAuthScheme <> currentDatabaseAuthScheme
                                || submittedDatabaseUsername <> currentDatabaseUsername
                                || submittedDatabasePassword.IsSome
                                || submittedUseSsl <> resource.State.UseSsl
                                || submittedEnableSshTunnel <> resource.State.EnableSshTunnel
                                || dtoTuples submittedConnectionOptions
                                   <> keyValueTuples resource.State.ConnectionOptions

                            do!
                                applyIfChanged
                                    databaseChanged
                                    (UpdateResourceDatabaseConfig(
                                        actor,
                                        resourceId,
                                        {
                                            DatabaseName = submittedDatabaseName
                                            DatabaseHost = submittedDatabaseHost
                                            DatabasePort = submittedDatabasePort
                                            DatabaseEngine = submittedDatabaseEngine
                                            DatabaseAuthScheme = submittedDatabaseAuthScheme
                                            DatabaseUsername = submittedDatabaseUsername
                                            DatabasePassword = submittedDatabasePassword
                                            UseSsl = submittedUseSsl
                                            EnableSshTunnel = submittedEnableSshTunnel
                                            ConnectionOptions = submittedConnectionOptions
                                        }
                                    ))

                        match firstError with
                        | Some error -> return! redirectWithFlash ctx target "error" (UiFormat.domainError error)
                        | None when changed -> return! redirectWithFlash ctx target "success" "Resource saved."
                        | None -> return! redirectWithFlash ctx target "info" "No resource changes to save."
        }

    let deleteResource (spaceId: string) (resourceId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryResourceId resourceId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/resources" "error" "Invalid resource id."
            | Some rid ->
                let! resource = (resources ctx).GetByIdAsync rid

                match resource with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/resources" "error" "Resource not found."
                | Some resource ->
                    let! allowed = hasSpacePermission ctx resource.State.SpaceId AuthRelation.ResourceDelete

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/resources"
                                "error"
                                "You do not have permission to delete resources."
                    else
                        let! result =
                            (resourceHandler ctx).HandleCommand(DeleteResource(UiContext.actorUserId ctx, resourceId))

                        match result with
                        | Ok _ ->
                            return! redirectWithFlash ctx $"/spaces/{spaceId}/resources" "success" "Resource deleted."
                        | Error error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/resources"
                                    "error"
                                    (UiFormat.domainError error)
        }

    let createApp (spaceId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx
            let folderId = formValue posted "FolderId"
            let resourceId = formValue posted "ResourceId"

            match tryFolderId folderId, tryResourceId resourceId with
            | Some fid, Some rid ->
                let! space = getSpaceFromFolder ctx fid
                let! resource = (resources ctx).GetByIdAsync rid

                match space, resource with
                | Some space, Some resource ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.AppCreate

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{folderId}"
                                "error"
                                "You do not have permission to create apps."
                    else
                        let submittedInputs = appInputs posted

                        let useDynamicJsonBody =
                            formValues posted "UseDynamicJsonBody" |> List.contains "true"

                        let sqlConfigResult =
                            if resource.State.ResourceKind = ResourceKind.Sql then
                                sqlQueryConfigFromForm posted
                            else
                                Ok None

                        match submittedInputs, sqlConfigResult with
                        | Error error, _
                        | _, Error error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{folderId}"
                                    "error"
                                    (UiFormat.domainError error)
                        | Ok inputs, Ok sqlConfig ->
                            let dto: CreateAppDto = {
                                Name = formValue posted "Name"
                                FolderId = folderId
                                ResourceId = resourceId
                                HttpMethod = optionalFormValue posted "HttpMethod" |> Option.defaultValue "GET"
                                Inputs = inputs
                                UrlPath = optionalFormValue posted "UrlPath"
                                UrlParameters = keyValuePairs posted "UrlParameter"
                                Headers = keyValuePairs posted "Header"
                                Body =
                                    if useDynamicJsonBody then
                                        []
                                    else
                                        keyValuePairs posted "Body"
                                UseDynamicJsonBody = useDynamicJsonBody
                                SqlConfig = sqlConfig
                                Description = optionalFormValue posted "Description"
                            }

                            match AppMapper.fromCreateDto dto with
                            | Error error ->
                                return!
                                    redirectWithFlash
                                        ctx
                                        $"/spaces/{spaceId}/{folderId}"
                                        "error"
                                        (UiFormat.domainError error)
                            | Ok request ->
                                match HttpMethod.Create request.HttpMethod with
                                | Error error ->
                                    return!
                                        redirectWithFlash
                                            ctx
                                            $"/spaces/{spaceId}/{folderId}"
                                            "error"
                                            (UiFormat.domainError error)
                                | Ok method ->
                                    match
                                        App.createWithSqlConfig
                                            (UiContext.actorUserId ctx)
                                            request.Name
                                            fid
                                            resource
                                            method
                                            request.Inputs
                                            request.UrlPath
                                            request.UrlParameters
                                            request.Headers
                                            request.Body
                                            request.UseDynamicJsonBody
                                            request.SqlConfig
                                            request.Description
                                    with
                                    | Error error ->
                                        return!
                                            redirectWithFlash
                                                ctx
                                                $"/spaces/{spaceId}/{folderId}"
                                                "error"
                                                (UiFormat.domainError error)
                                    | Ok app ->
                                        let! result =
                                            (appHandler ctx).HandleCommand(CreateApp(UiContext.actorUserId ctx, app))

                                        match result with
                                        | Ok(AppResult created) ->
                                            return!
                                                redirectWithFlash
                                                    ctx
                                                    $"/spaces/{spaceId}/{created.Id.Value}"
                                                    "success"
                                                    "App created."
                                        | Error error ->
                                            return!
                                                redirectWithFlash
                                                    ctx
                                                    $"/spaces/{spaceId}/{folderId}"
                                                    "error"
                                                    (UiFormat.domainError error)
                                        | _ ->
                                            return!
                                                redirectWithFlash
                                                    ctx
                                                    $"/spaces/{spaceId}/{folderId}"
                                                    "error"
                                                    "Unexpected app result."
                | _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Folder or resource not found."
            | _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid folder or resource id."
        }

    let updateAppName (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryAppId appId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid app id."
            | Some aid ->
                let! space = getSpaceFromApp ctx aid

                match space with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                | Some space when space.Id.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.AppEdit

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{appId}"
                                "error"
                                "You do not have permission to edit apps."
                    else
                        let! result =
                            (appHandler ctx)
                                .HandleCommand(
                                    UpdateAppName(UiContext.actorUserId ctx, appId, { Name = formValue posted "Name" })
                                )

                        match result with
                        | Ok _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}/{appId}" "success" "App renamed."
                        | Error error ->
                            return!
                                redirectWithFlash ctx $"/spaces/{spaceId}/{appId}" "error" (UiFormat.domainError error)
        }

    let updateAppDescription (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryAppId appId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid app id."
            | Some aid ->
                let! space = getSpaceFromApp ctx aid

                match space with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                | Some space when space.Id.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.AppEdit

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{appId}"
                                "error"
                                "You do not have permission to edit apps."
                    else
                        let! result =
                            (appHandler ctx)
                                .HandleCommand(
                                    UpdateAppDescription(
                                        UiContext.actorUserId ctx,
                                        appId,
                                        {
                                            Description = optionalFormValue posted "Description"
                                        }
                                    )
                                )

                        match result with
                        | Ok _ ->
                            return! redirectWithFlash ctx $"/spaces/{spaceId}/{appId}" "success" "Description saved."
                        | Error error ->
                            return!
                                redirectWithFlash ctx $"/spaces/{spaceId}/{appId}" "error" (UiFormat.domainError error)
        }

    let updateAppConfig (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryAppId appId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid app id."
            | Some aid ->
                let! space = getSpaceFromApp ctx aid

                match space with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                | Some space when space.Id.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.AppEdit

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{appId}"
                                "error"
                                "You do not have permission to edit apps."
                    else
                        let appRepository = apps ctx
                        let resourceRepository = resources ctx
                        let! currentApp = appRepository.GetByIdAsync aid

                        match currentApp with
                        | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                        | Some currentApp ->
                            let! selectedResource = resourceRepository.GetByIdAsync currentApp.State.ResourceId

                            match selectedResource with
                            | None ->
                                return!
                                    redirectWithFlash ctx $"/spaces/{spaceId}/{appId}" "error" "App resource not found."
                            | Some selectedResource ->
                                match appInputs posted with
                                | Error error ->
                                    return!
                                        redirectWithFlash
                                            ctx
                                            $"/spaces/{spaceId}/{appId}"
                                            "error"
                                            (UiFormat.domainError error)
                                | Ok inputs ->
                                    let httpMethod =
                                        optionalFormValue posted "HttpMethod"
                                        |> Option.defaultValue (currentApp.State.HttpMethod.ToString())

                                    let urlPath = optionalFormValue posted "UrlPath"
                                    let useDynamic = formValues posted "UseDynamicJsonBody" |> List.contains "true"
                                    let actor = UiContext.actorUserId ctx
                                    let handler = appHandler ctx

                                    let handleWithResource command =
                                        AppHandler.handleCommandWithResourceRepository
                                            appRepository
                                            resourceRepository
                                            command

                                    let! inputsResult =
                                        handler.HandleCommand(UpdateAppInputs(actor, appId, { Inputs = inputs }))

                                    let! methodResult =
                                        handler.HandleCommand(
                                            UpdateAppHttpMethod(actor, appId, { HttpMethod = httpMethod })
                                        )

                                    let! operationResults = task {
                                        match selectedResource.State.ResourceKind with
                                        | ResourceKind.Http ->
                                            let body = if useDynamic then [] else keyValuePairs posted "Body"

                                            let! pathResult =
                                                handler.HandleCommand(
                                                    UpdateAppUrlPath(actor, appId, { UrlPath = urlPath })
                                                )

                                            let! dynamicResult =
                                                handler.HandleCommand(
                                                    UpdateAppUseDynamicJsonBody(
                                                        actor,
                                                        appId,
                                                        { UseDynamicJsonBody = useDynamic }
                                                    )
                                                )

                                            let! queryResult =
                                                handleWithResource (
                                                    UpdateAppQueryParameters(
                                                        actor,
                                                        appId,
                                                        {
                                                            UrlParameters = keyValuePairs posted "UrlParameter"
                                                        }
                                                    )
                                                )

                                            let! headersResult =
                                                handleWithResource (
                                                    UpdateAppHeaders(
                                                        actor,
                                                        appId,
                                                        {
                                                            Headers = keyValuePairs posted "Header"
                                                        }
                                                    )
                                                )

                                            let! bodyResult =
                                                handleWithResource (UpdateAppBody(actor, appId, { Body = body }))

                                            return [
                                                inputsResult
                                                methodResult
                                                pathResult
                                                dynamicResult
                                                queryResult
                                                headersResult
                                                bodyResult
                                            ]
                                        | ResourceKind.Sql ->
                                            match sqlQueryConfigFromForm posted with
                                            | Error error -> return [ inputsResult; methodResult; Error error ]
                                            | Ok sqlConfig ->
                                                let! sqlResult =
                                                    handleWithResource (
                                                        UpdateAppSqlConfig(actor, appId, { SqlConfig = sqlConfig })
                                                    )

                                                return [ inputsResult; methodResult; sqlResult ]
                                    }

                                    let firstError =
                                        operationResults
                                        |> List.tryPick (function
                                            | Error error -> Some error
                                            | _ -> None)

                                    match firstError with
                                    | Some error ->
                                        return!
                                            redirectWithFlash
                                                ctx
                                                $"/spaces/{spaceId}/{appId}"
                                                "error"
                                                (UiFormat.domainError error)
                                    | None ->
                                        return!
                                            redirectWithFlash
                                                ctx
                                                $"/spaces/{spaceId}/{appId}"
                                                "success"
                                                "App configuration saved."
        }

    let runApp (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryAppId appId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/{appId}/run" "error" "Invalid app id."
            | Some aid ->
                let! space = getSpaceFromApp ctx aid
                let! app = (apps ctx).GetByIdAsync aid

                match space, app with
                | Some space, Some app ->
                    if space.Id.Value.ToString() <> spaceId then
                        return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
                    else
                        let! allowed = hasSpacePermission ctx space.Id AuthRelation.AppRun

                        if not allowed then
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{appId}/run"
                                    "error"
                                    "You do not have permission to run apps."
                        else
                            let inputs: RunInputDto list =
                                app.State.Inputs
                                |> List.map (fun input -> {
                                    Title = input.Title
                                    Value = formValue posted input.Title
                                })

                            let dynamicBody = keyValuePairs posted "DynamicBody"
                            let! currentUser = getCurrentUser ctx

                            match currentUser with
                            | Error error ->
                                return!
                                    redirectWithFlash
                                        ctx
                                        $"/spaces/{spaceId}/{appId}/run"
                                        "error"
                                        (UiFormat.domainError error)
                            | Ok currentUser ->
                                let! result =
                                    RunHandler.handleCommand
                                        (runs ctx)
                                        (apps ctx)
                                        (resources ctx)
                                        (sqlExecution ctx)
                                        (CreateRun(
                                            UiContext.actorUserId ctx,
                                            appId,
                                            currentUser,
                                            {
                                                InputValues = inputs
                                                DynamicBody =
                                                    if List.isEmpty dynamicBody then None else Some dynamicBody
                                            }
                                        ))

                                match result with
                                | Ok(RunResult run) ->
                                    ctx.Response.Headers.CacheControl <- "no-store"
                                    let sanitized = ResponseSanitizer.sanitizeRun run

                                    let! model =
                                        UiContext.layoutModel
                                            ctx
                                            "spaces"
                                            $"Run {app.State.Name}"
                                            (Some {
                                                Tone = FlashTone.Success
                                                Message = "Run completed."
                                            })

                                    return!
                                        ctx.WriteHtmlView(
                                            LayoutView.page
                                                model
                                                (Views.runAppPage
                                                    (UiContext.antiforgeryToken ctx)
                                                    space
                                                    (ResponseSanitizer.sanitizeApp app.State)
                                                    (Some sanitized))
                                        )
                                | Ok _ ->
                                    return!
                                        redirectWithFlash
                                            ctx
                                            $"/spaces/{spaceId}/{appId}/run"
                                            "error"
                                            "Unexpected run result."
                                | Error error ->
                                    return!
                                        redirectWithFlash
                                            ctx
                                            $"/spaces/{spaceId}/{appId}/run"
                                            "error"
                                            (UiFormat.domainError error)
                | _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "App not found."
        }

    let createDashboard (spaceId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx
            let folderId = formValue posted "FolderId"

            match tryFolderId folderId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid folder id."
            | Some fid ->
                let! space = getSpaceFromFolder ctx fid

                match space with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Folder not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.DashboardCreate

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{folderId}"
                                "error"
                                "You do not have permission to create dashboards."
                    else
                        let dto = {
                            Name = formValue posted "Name"
                            FolderId = folderId
                            PrepareAppId = None
                            Configuration = "{}"
                        }

                        let! result =
                            (dashboardHandler ctx).HandleCommand(CreateDashboard(UiContext.actorUserId ctx, dto))

                        match result with
                        | Ok(DashboardResult dashboard) ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{dashboard.Id.Value}"
                                    "success"
                                    "Dashboard created."
                        | Error error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{folderId}"
                                    "error"
                                    (UiFormat.domainError error)
                        | _ ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{folderId}"
                                    "error"
                                    "Unexpected dashboard result."
        }

    let updateDashboardName (spaceId: string) (dashboardId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryDashboardId dashboardId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid dashboard id."
            | Some did ->
                let! space = getSpaceFromDashboard ctx did

                match space with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Dashboard not found."
                | Some space when space.Id.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Dashboard not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.DashboardEdit

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{dashboardId}"
                                "error"
                                "You do not have permission to edit dashboards."
                    else
                        let! result =
                            (dashboardHandler ctx)
                                .HandleCommand(
                                    UpdateDashboardName(
                                        UiContext.actorUserId ctx,
                                        dashboardId,
                                        { Name = formValue posted "Name" }
                                    )
                                )

                        match result with
                        | Ok _ ->
                            return!
                                redirectWithFlash ctx $"/spaces/{spaceId}/{dashboardId}" "success" "Dashboard renamed."
                        | Error error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{dashboardId}"
                                    "error"
                                    (UiFormat.domainError error)
        }

    let updateDashboardConfig (spaceId: string) (dashboardId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryDashboardId dashboardId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Invalid dashboard id."
            | Some did ->
                let! space = getSpaceFromDashboard ctx did

                match space with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Dashboard not found."
                | Some space when space.Id.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Dashboard not found."
                | Some space ->
                    let! allowed = hasSpacePermission ctx space.Id AuthRelation.DashboardEdit

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/{dashboardId}"
                                "error"
                                "You do not have permission to edit dashboards."
                    else
                        let prepareAppId = optionalFormValue posted "PrepareAppId"
                        let configuration = formValue posted "Configuration"
                        let handler = dashboardHandler ctx
                        let actor = UiContext.actorUserId ctx

                        let! prepResult =
                            handler.HandleCommand(
                                UpdateDashboardPrepareApp(actor, dashboardId, { PrepareAppId = prepareAppId })
                            )

                        let! configResult =
                            handler.HandleCommand(
                                UpdateDashboardConfiguration(actor, dashboardId, { Configuration = configuration })
                            )

                        let firstError =
                            [ prepResult; configResult ]
                            |> List.tryPick (function
                                | Error error -> Some error
                                | _ -> None)

                        match firstError with
                        | Some error ->
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{dashboardId}"
                                    "error"
                                    (UiFormat.domainError error)
                        | None ->
                            return!
                                redirectWithFlash ctx $"/spaces/{spaceId}/{dashboardId}" "success" "Dashboard saved."
        }

    let prepareDashboard (spaceId: string) (dashboardId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx
            let! posted = form ctx

            match tryDashboardId dashboardId with
            | None ->
                return!
                    redirectWithFlash
                        ctx
                        $"/spaces/{spaceId}/{dashboardId}/dashboard-run"
                        "error"
                        "Invalid dashboard id."
            | Some did ->
                let! space = getSpaceFromDashboard ctx did
                let! dashboard = (dashboards ctx).GetByIdAsync did

                match space, dashboard with
                | Some space, Some dashboard ->
                    if space.Id.Value.ToString() <> spaceId then
                        return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Dashboard not found."
                    else
                        let! canRunDashboard = hasSpacePermission ctx space.Id AuthRelation.DashboardRun
                        let! canRunApps = hasSpacePermission ctx space.Id AuthRelation.AppRun

                        if not canRunDashboard || not canRunApps then
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/{dashboardId}/dashboard-run"
                                    "error"
                                    "You need run_dashboard and run_app permissions."
                        else
                            let loadInputs: RunInputDto list =
                                List.zip (formValues posted "LoadInputKey") (formValues posted "LoadInputValue")
                                |> List.map (fun (key, value) -> { Title = key; Value = value })

                            let! currentUser = getCurrentUser ctx

                            match currentUser with
                            | Error error ->
                                return!
                                    redirectWithFlash
                                        ctx
                                        $"/spaces/{spaceId}/{dashboardId}/dashboard-run"
                                        "error"
                                        (UiFormat.domainError error)
                            | Ok currentUser ->
                                let! result =
                                    (dashboardHandler ctx)
                                        .HandleCommand(
                                            PrepareDashboard(
                                                UiContext.actorUserId ctx,
                                                dashboardId,
                                                currentUser,
                                                { LoadInputs = loadInputs }
                                            )
                                        )

                                match result with
                                | Ok(DashboardPrepareResult response) ->
                                    ctx.Response.Headers.CacheControl <- "no-store"

                                    let text =
                                        $"Status: {response.Status}\nPrepare run: {response.PrepareRunId}\nResponse: {response.Response |> Option.defaultValue String.Empty}\nError: {response.ErrorMessage |> Option.defaultValue String.Empty}"

                                    let! model =
                                        UiContext.layoutModel
                                            ctx
                                            "spaces"
                                            $"Run {dashboard.State.Name.Value}"
                                            (Some {
                                                Tone = FlashTone.Success
                                                Message = "Dashboard prepared."
                                            })

                                    return!
                                        ctx.WriteHtmlView(
                                            LayoutView.page
                                                model
                                                (Views.dashboardRuntimePage
                                                    (UiContext.antiforgeryToken ctx)
                                                    space
                                                    dashboard.State
                                                    (Some text))
                                        )
                                | Ok _ ->
                                    return!
                                        redirectWithFlash
                                            ctx
                                            $"/spaces/{spaceId}/{dashboardId}/dashboard-run"
                                            "error"
                                            "Unexpected dashboard result."
                                | Error error ->
                                    return!
                                        redirectWithFlash
                                            ctx
                                            $"/spaces/{spaceId}/{dashboardId}/dashboard-run"
                                            "error"
                                            (UiFormat.domainError error)
                | _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}" "error" "Dashboard not found."
        }

    let restoreApp (spaceId: string) (appId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryAppId appId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Invalid app id."
            | Some aid ->
                let! app = (apps ctx).GetDeletedByIdAsync aid

                match app with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "App not found in trash."
                | Some app ->
                    let! space = getSpaceFromFolderIncludingDeleted ctx app.State.FolderId

                    match space with
                    | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "App space not found."
                    | Some space when space.Id.Value.ToString() <> spaceId ->
                        return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "App not found in this space."
                    | Some space ->
                        let! allowed = hasSpacePermission ctx space.Id AuthRelation.AppDelete

                        if not allowed then
                            return!
                                redirectWithFlash
                                    ctx
                                    $"/spaces/{spaceId}/trash"
                                    "error"
                                    "You do not have permission to restore deleted apps."
                        else
                            let! result = (trashHandler ctx).HandleCommand(RestoreApp(UiContext.actorUserId ctx, appId))

                            match result with
                            | Ok _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "success" "App restored."
                            | Error error ->
                                return!
                                    redirectWithFlash
                                        ctx
                                        $"/spaces/{spaceId}/trash"
                                        "error"
                                        (UiFormat.domainError error)
        }

    let restoreFolder (spaceId: string) (folderId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryFolderId folderId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Invalid folder id."
            | Some fid ->
                let! folder = (folders ctx).GetDeletedByIdAsync fid

                match folder with
                | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Folder not found in trash."
                | Some folder when folder.State.SpaceId.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Folder not found in this space."
                | Some folder ->
                    let! allowed = hasSpacePermission ctx folder.State.SpaceId AuthRelation.FolderDelete

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/trash"
                                "error"
                                "You do not have permission to restore deleted folders."
                    else
                        let! result =
                            (trashHandler ctx).HandleCommand(RestoreFolder(UiContext.actorUserId ctx, folderId))

                        match result with
                        | Ok _ -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "success" "Folder restored."
                        | Error error ->
                            return!
                                redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" (UiFormat.domainError error)
        }

    let restoreResource (spaceId: string) (resourceId: string) : EndpointHandler =
        fun ctx -> task {
            do! validatePost ctx

            match tryResourceId resourceId with
            | None -> return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Invalid resource id."
            | Some rid ->
                let! resource = (resources ctx).GetDeletedByIdAsync rid

                match resource with
                | None ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Resource not found in trash."
                | Some resource when resource.State.SpaceId.Value.ToString() <> spaceId ->
                    return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" "Resource not found in this space."
                | Some resource ->
                    let! allowed = hasSpacePermission ctx resource.State.SpaceId AuthRelation.ResourceDelete

                    if not allowed then
                        return!
                            redirectWithFlash
                                ctx
                                $"/spaces/{spaceId}/trash"
                                "error"
                                "You do not have permission to restore deleted resources."
                    else
                        let! result =
                            (trashHandler ctx).HandleCommand(RestoreResource(UiContext.actorUserId ctx, resourceId))

                        match result with
                        | Ok _ ->
                            return! redirectWithFlash ctx $"/spaces/{spaceId}/trash" "success" "Resource restored."
                        | Error error ->
                            return!
                                redirectWithFlash ctx $"/spaces/{spaceId}/trash" "error" (UiFormat.domainError error)
        }