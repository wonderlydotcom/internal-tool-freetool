namespace Freetool.Api.Ui

open System
open Oxpecker.ViewEngine
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

module Views =
    let private idOf (id: Guid) = id.ToString()
    let private spaceId (space: SpaceData) = space.Id.Value.ToString()
    let private folderId (folder: FolderData) = folder.Id.Value.ToString()
    let private appId (app: AppData) = app.Id.Value.ToString()
    let private dashboardId (dashboard: DashboardData) = dashboard.Id.Value.ToString()
    let private resourceId (resource: ResourceData) = resource.Id.Value.ToString()
    let private userId (user: UserData) = user.Id.Value.ToString()

    let private displayUserName (user: UserData) =
        if String.IsNullOrWhiteSpace user.Name then user.Email else user.Name

    let private selectedUserName (users: UserData list) (id: UserId) =
        users
        |> List.tryFind (fun user -> user.Id = id)
        |> Option.map displayUserName
        |> Option.defaultValue (id.Value.ToString())

    let private userInitials (user: UserData) =
        let source = displayUserName user

        let parts =
            source.Split([| ' '; '.'; '@'; '-' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.truncate 2
            |> Array.choose (fun part ->
                if String.IsNullOrWhiteSpace part then None else Some(part.Substring(0, 1).ToUpperInvariant()))

        if Array.isEmpty parts then "?" else String.concat String.Empty parts

    let private userAvatar (user: UserData) =
        span (class' = "avatar") {
            match user.ProfilePicUrl with
            | Some url when not (String.IsNullOrWhiteSpace url) ->
                UiHtml.attrs [ "src", url; "alt", displayUserName user ] (img ())
            | _ -> userInitials user
        }

    let private cardHeader (title: string) (subtitle: string option) : HtmlElement =
        div (class' = "card-header") {
            h2 () { title }
            match subtitle with
            | Some value -> p () { value }
            | None -> ()
        }

    let private emptyState (title: string) (message: string) : HtmlElement =
        div (class' = "empty-state") {
            h2 () { title }
            p () { message }
        }

    let private field (labelText: string) (inputElement: HtmlElement) (helpText: string option) =
        label (class' = "field") {
            span () { labelText }
            inputElement
            match helpText with
            | Some text -> small () { text }
            | None -> ()
        }

    let private srOnly (text: string) : HtmlElement = span (class' = "sr-only") { text }

    let private iconOnlyButton (labelText: string) (iconText: string) (extraAttrs: (string * string) list) : HtmlElement =
        let tag =
            UiHtml.attrs
                ([ "type", "button"
                   "class", "icon-button"
                   "aria-label", labelText
                   "title", labelText ]
                 @ extraAttrs)
                (button ())

        tag {
            span () { iconText }
            srOnly labelText
        }

    let private iconOnlyLink (href: string) (labelText: string) (iconText: string) : HtmlElement =
        let tag =
            UiHtml.attrs
                [ "href", href
                  "class", "icon-button"
                  "aria-label", labelText
                  "title", labelText ]
                (a ())

        tag {
            span () { iconText }
            srOnly labelText
        }

    let private iconDeleteForm (token: string) (action: string) (labelText: string) (confirmText: string) : HtmlElement =
        let formTag =
            UiHtml.enhancedPostForm action [ "class", "inline-icon-form"; "data-confirm", confirmText ]

        formTag {
            UiHtml.antiforgeryInput token

            let buttonTag =
                UiHtml.attrs
                    [ "type", "submit"
                      "class", "icon-button icon-button-danger"
                      "aria-label", labelText
                      "title", labelText ]
                    (button ())

            buttonTag {
                span () { "🗑️" }
                srOnly labelText
            }
        }

    let private modalOpenButton (modalId: string) (labelText: string) (iconText: string) (buttonClass: string) : HtmlElement =
        let tag =
            UiHtml.attrs
                [ "type", "button"
                  "class", buttonClass
                  "data-modal-open", modalId
                  "aria-label", labelText ]
                (button ())

        tag {
            span () { iconText }
            span () { labelText }
        }

    let private iconModalOpenButton (modalId: string) (labelText: string) (iconText: string) : HtmlElement =
        iconOnlyButton labelText iconText [ "data-modal-open", modalId ]

    let private modalDialog (modalId: string) (title: string) (subtitle: string option) (content: HtmlElement) =
        dialog (id = modalId, class' = "modal") {
            section (class' = "modal-card") {
                header (class' = "modal-header") {
                    div () {
                        h2 () { title }
                        match subtitle with
                        | Some text -> p () { text }
                        | None -> ()
                    }

                    let closeButton =
                        UiHtml.attrs
                            [ "type", "button"
                              "class", "modal-close"
                              "data-modal-close", "true"
                              "aria-label", "Close" ]
                            (button ())

                    closeButton { "×" }
                }

                div (class' = "modal-scroll") { content }
            }
        }

    let private kvRows (namePrefix: string) (pairs: KeyValuePairDto list) =
        let container = UiHtml.attrs [ "class", "kv-rows"; "data-kv-rows", "true" ] (div ())

        container {
            for _, pair in pairs |> List.indexed do
                div (class' = "kv-row") {
                    UiHtml.textInput $"{namePrefix}Key" pair.Key false "Key"
                    UiHtml.textInput $"{namePrefix}Value" pair.Value false "Value"
                    let removeButton = UiHtml.attrs [ "type", "button"; "class", "button button-ghost"; "data-remove-row", "true" ] (button ())
                    removeButton { "Remove" }
                }

            div (class' = "kv-row kv-row-template") {
                UiHtml.textInput $"{namePrefix}Key" String.Empty false "Key"
                UiHtml.textInput $"{namePrefix}Value" String.Empty false "Value"
                let removeButton = UiHtml.attrs [ "type", "button"; "class", "button button-ghost"; "data-remove-row", "true" ] (button ())
                removeButton { "Remove" }
            }

            let addButton = UiHtml.attrs [ "type", "button"; "class", "button button-secondary"; "data-add-kv-row", "true" ] (button ())
            addButton { "Add row" }
        }

    let private spaceTabs (space: SpaceData) (active: string) =
        let sid = spaceId space
        nav (class' = "tabs") {
            let tab (href: string) (key: string) (labelText: string) : HtmlElement =
                let tag = UiHtml.attrs [ "href", href; "class", if active = key then "tab is-active" else "tab" ] (a ())
                tag { labelText }

            tab $"/spaces/{sid}" "builder" "Builder"
            tab $"/spaces/{sid}/resources" "resources" "Resources"
            tab $"/spaces/{sid}/settings" "settings" "Settings"
            tab $"/spaces/{sid}/trash" "trash" "Trash"
        }

    let private objectIcon kind =
        match kind with
        | "Folder" -> "📁"
        | "App" -> "⚡"
        | "Dashboard" -> "📊"
        | "Resource" -> "🔌"
        | _ -> "•"

    let private objectCard
        (title: string)
        (kind: string)
        (href: string)
        (description: string option)
        (actions: HtmlElement list)
        =
        article (class' = "object-card") {
            a (href = href, class' = "object-card-link", title = kind) {
                span (class' = "object-icon") { objectIcon kind }
                h3 () { title }
                match description with
                | Some value when not (String.IsNullOrWhiteSpace value) -> p () { value }
                | _ -> ()
            }

            if not (List.isEmpty actions) then
                div (class' = "object-actions") {
                    for action in actions do
                        action
                }
        }

    let private createSpaceForm (token: string) (users: UserData list) =
        let action = "/_ui/spaces/create"
        let formTag = UiHtml.enhancedPostForm action []

        formTag {
            UiHtml.antiforgeryInput token
            field "Name" (UiHtml.textInput "Name" String.Empty true "Support tools") None
            label (class' = "field") {
                span () { "Moderator" }
                let selectTag = UiHtml.attrs [ "name", "ModeratorUserId"; "required", "required" ] (select ())
                selectTag {
                    option (value = "") { "Select a moderator" }
                    for user in users do
                        option (value = userId user) { $"{displayUserName user} ({user.Email})" }
                }
            }
            div (class' = "field") {
                span () { "Members" }
                div (class' = "checkbox-grid") {
                    for user in users do
                        label () {
                            UiHtml.checkbox "MemberIds" (userId user) false
                            span () { $"{displayUserName user} ({user.Email})" }
                        }
                }
            }
            field "Invite by email" (UiHtml.textInput "InviteEmail" String.Empty false "new.user@example.com") (Some "Invited users are created as pending users and added as members.")
            UiHtml.submitButton "Create space"
        }

    let private createFolderForm (token: string) (sid: string) (parentId: string option) =
        let action = $"/_ui/spaces/{sid}/folders/create"
        let formTag = UiHtml.enhancedPostForm action []

        formTag {
            UiHtml.antiforgeryInput token
            UiHtml.hidden "ParentId" (parentId |> Option.defaultValue String.Empty)
            field "Folder name" (UiHtml.textInput "Name" String.Empty true "New folder") None
            UiHtml.submitButton "Create folder"
        }

    let private renameFolderForm (token: string) (sid: string) (fid: string) (currentName: string) =
        let action = $"/_ui/spaces/{sid}/folders/{fid}/rename"
        let formTag = UiHtml.enhancedPostForm action []

        formTag {
            UiHtml.antiforgeryInput token
            field "Folder name" (UiHtml.textInput "Name" currentName true "Folder name") None
            UiHtml.submitButton "Rename folder"
        }

    let private createAppForm (token: string) (sid: string) (fid: string) (resources: ResourceData list) =
        let action = $"/_ui/spaces/{sid}/apps/create"
        let formTag = UiHtml.enhancedPostForm action []

        formTag {
            UiHtml.antiforgeryInput token
            UiHtml.hidden "FolderId" fid
            field "App name" (UiHtml.textInput "Name" String.Empty true "Customer lookup") None
            label (class' = "field") {
                span () { "Resource" }
                let selectTag = UiHtml.attrs [ "name", "ResourceId"; "required", "required" ] (select ())
                selectTag {
                    option (value = "") { "Select resource" }
                    for resource in resources do
                        option (value = resourceId resource) { resource.Name.Value }
                }
            }
            UiHtml.submitButton "Create app"
        }

    let private createDashboardForm (token: string) (sid: string) (fid: string) =
        let action = $"/_ui/spaces/{sid}/dashboards/create"
        let formTag = UiHtml.enhancedPostForm action []

        formTag {
            UiHtml.antiforgeryInput token
            UiHtml.hidden "FolderId" fid
            field "Dashboard name" (UiHtml.textInput "Name" String.Empty true "Ops dashboard") None
            UiHtml.submitButton "Create dashboard"
        }

    let noSpaces (token: string) (isOrgAdmin: bool) (users: UserData list) =
        let modalId = "create-space-modal"

        section (class' = "stack") {
            emptyState "No spaces yet" "Create a space to start building internal tools."

            if isOrgAdmin then
                div (class' = "actions") { modalOpenButton modalId "New Space" "+" "button" }
                modalDialog modalId "Create space" (Some "Spaces group resources, folders, apps, dashboards, and permissions.") (createSpaceForm token users)
        }

    let spacesList (token: string) (spaces: SpaceData list) (users: UserData list) (isOrgAdmin: bool) =
        let modalId = "create-space-modal"

        section (class' = "stack") {
            section (class' = "toolbar") {
                div () {
                    h2 () { "Spaces" }
                    p () { $"{List.length spaces} visible spaces" }
                }

                if isOrgAdmin then
                    modalOpenButton modalId "New Space" "+" "button"
            }

            section (class' = "card") {
                div (class' = "spaces-grid") {
                    for space in spaces do
                        let sid = spaceId space
                        article (class' = "space-card") {
                            a (href = $"/spaces/{sid}", class' = "space-card-link") {
                                h3 () { space.Name }
                                p () {
                                    $"Moderator: {selectedUserName users space.ModeratorUserId} · Members: {List.length space.MemberIds}"
                                }
                            }
                            div (class' = "space-card-actions") {
                                iconOnlyLink $"/spaces/{sid}/settings" $"{space.Name} settings" "⚙️"
                            }
                        }
                }
            }

            if isOrgAdmin then
                modalDialog modalId "Create space" (Some "Organization administrators can create spaces.") (createSpaceForm token users)
        }

    let spaceHome
        (token: string)
        (space: SpaceData)
        (folders: FolderData list)
        (apps: AppData list)
        (dashboards: DashboardData list)
        (resources: ResourceData list)
        (permissions: SpacePermissionsDto)
        =
        let sid = spaceId space
        let createFolderModalId = $"create-folder-{sid}"

        section (class' = "stack") {
            spaceTabs space "builder"
            section (class' = "card") {
                div (class' = "toolbar") {
                    div () { cardHeader space.Name (Some "Root folder") }
                    div (class' = "actions") {
                        if permissions.CreateFolder then
                            modalOpenButton createFolderModalId "New Folder" "+" "button"
                    }
                }
            }

            section (class' = "object-grid") {
                for folder in folders do
                    let fid = folderId folder
                    let renameModalId = $"rename-folder-{fid}"
                    let subfolderModalId = $"create-subfolder-{fid}"

                    let actions =
                        [
                            if permissions.CreateFolder then
                                iconModalOpenButton subfolderModalId "New subfolder" "➕"
                            if permissions.EditFolder then
                                iconModalOpenButton renameModalId "Rename folder" "✏️"
                            if permissions.DeleteFolder then
                                iconDeleteForm token $"/_ui/spaces/{sid}/folders/{fid}/delete" "Delete folder" $"Delete folder {folder.Name.Value}?"
                        ]

                    objectCard folder.Name.Value "Folder" $"/spaces/{sid}/{fid}" None actions
                    modalDialog subfolderModalId $"New subfolder in {folder.Name.Value}" None (createFolderForm token sid (Some fid))
                    modalDialog renameModalId $"Rename {folder.Name.Value}" None (renameFolderForm token sid fid folder.Name.Value)
                for app in apps do
                    let aid = appId app
                    let actions =
                        [
                            if permissions.RunApp then iconOnlyLink $"/spaces/{sid}/{aid}/run" "Run app" "▶️"
                            if permissions.EditApp then iconOnlyLink $"/spaces/{sid}/{aid}" "Edit app" "✏️"
                            if permissions.DeleteApp then
                                iconDeleteForm token $"/_ui/spaces/{sid}/apps/{aid}/delete" "Delete app" $"Delete app {app.Name}?"
                        ]

                    objectCard app.Name "App" $"/spaces/{sid}/{aid}" app.Description actions
                for dashboard in dashboards do
                    let did = dashboardId dashboard
                    let actions =
                        [
                            if permissions.RunDashboard then iconOnlyLink $"/spaces/{sid}/{did}/dashboard-run" "Run dashboard" "▶️"
                            if permissions.EditDashboard then iconOnlyLink $"/spaces/{sid}/{did}" "Edit dashboard" "✏️"
                            if permissions.DeleteDashboard then
                                iconDeleteForm token $"/_ui/spaces/{sid}/dashboards/{did}/delete" "Delete dashboard" $"Delete dashboard {dashboard.Name.Value}?"
                        ]

                    objectCard dashboard.Name.Value "Dashboard" $"/spaces/{sid}/{did}" None actions
            }

            if permissions.CreateFolder then
                modalDialog createFolderModalId "New folder" (Some $"Create a folder in {space.Name}.") (createFolderForm token sid None)

            if List.isEmpty folders && List.isEmpty apps && List.isEmpty dashboards then
                emptyState "This space is empty" "Create folders first, then add apps and dashboards inside them."
        }

    let folderPage
        (token: string)
        (space: SpaceData)
        (folder: FolderData)
        (childFolders: FolderData list)
        (apps: AppData list)
        (dashboards: DashboardData list)
        (resources: ResourceData list)
        (permissions: SpacePermissionsDto)
        =
        let sid = spaceId space
        let fid = folderId folder
        let renameModalId = $"rename-folder-{fid}"
        let createFolderModalId = $"create-folder-{fid}"
        let createAppModalId = $"create-app-{fid}"
        let createDashboardModalId = $"create-dashboard-{fid}"

        section (class' = "stack") {
            spaceTabs space "builder"
            section (class' = "card") {
                div (class' = "toolbar") {
                    div () { cardHeader folder.Name.Value (Some "Folder") }
                    div (class' = "actions") {
                        if permissions.CreateFolder then
                            modalOpenButton createFolderModalId "New Folder" "+" "button button-secondary"
                        if permissions.CreateApp && not (List.isEmpty resources) then
                            modalOpenButton createAppModalId "New App" "+" "button button-secondary"
                        if permissions.CreateDashboard then
                            modalOpenButton createDashboardModalId "New Dashboard" "+" "button button-secondary"
                        if permissions.EditFolder then
                            modalOpenButton renameModalId "Rename" "✏️" "button button-ghost"
                        if permissions.DeleteFolder then
                            iconDeleteForm token $"/_ui/spaces/{sid}/folders/{fid}/delete" "Delete folder" $"Delete folder {folder.Name.Value}?"
                    }
                }

                if permissions.CreateApp && List.isEmpty resources then
                    p () { "Create a resource before adding apps to this folder." }
            }

            section (class' = "object-grid") {
                for child in childFolders do
                    let childId = folderId child
                    let childRenameModalId = $"rename-folder-{childId}"
                    let childSubfolderModalId = $"create-subfolder-{childId}"

                    let actions =
                        [
                            if permissions.CreateFolder then
                                iconModalOpenButton childSubfolderModalId "New subfolder" "➕"
                            if permissions.EditFolder then
                                iconModalOpenButton childRenameModalId "Rename folder" "✏️"
                            if permissions.DeleteFolder then
                                iconDeleteForm token $"/_ui/spaces/{sid}/folders/{childId}/delete" "Delete folder" $"Delete folder {child.Name.Value}?"
                        ]

                    objectCard child.Name.Value "Folder" $"/spaces/{sid}/{childId}" None actions
                    modalDialog childSubfolderModalId $"New subfolder in {child.Name.Value}" None (createFolderForm token sid (Some childId))
                    modalDialog childRenameModalId $"Rename {child.Name.Value}" None (renameFolderForm token sid childId child.Name.Value)
                for app in apps do
                    let aid = appId app
                    let actions =
                        [
                            if permissions.RunApp then iconOnlyLink $"/spaces/{sid}/{aid}/run" "Run app" "▶️"
                            if permissions.EditApp then iconOnlyLink $"/spaces/{sid}/{aid}" "Edit app" "✏️"
                            if permissions.DeleteApp then
                                iconDeleteForm token $"/_ui/spaces/{sid}/apps/{aid}/delete" "Delete app" $"Delete app {app.Name}?"
                        ]

                    objectCard app.Name "App" $"/spaces/{sid}/{aid}" app.Description actions
                for dashboard in dashboards do
                    let did = dashboardId dashboard
                    let actions =
                        [
                            if permissions.RunDashboard then iconOnlyLink $"/spaces/{sid}/{did}/dashboard-run" "Run dashboard" "▶️"
                            if permissions.EditDashboard then iconOnlyLink $"/spaces/{sid}/{did}" "Edit dashboard" "✏️"
                            if permissions.DeleteDashboard then
                                iconDeleteForm token $"/_ui/spaces/{sid}/dashboards/{did}/delete" "Delete dashboard" $"Delete dashboard {dashboard.Name.Value}?"
                        ]

                    objectCard dashboard.Name.Value "Dashboard" $"/spaces/{sid}/{did}" None actions
            }

            if permissions.CreateFolder then
                modalDialog createFolderModalId "New subfolder" (Some $"Create a folder in {folder.Name.Value}.") (createFolderForm token sid (Some fid))
            if permissions.CreateApp && not (List.isEmpty resources) then
                modalDialog createAppModalId "New app" (Some $"Create an app in {folder.Name.Value}.") (createAppForm token sid fid resources)
            if permissions.CreateDashboard then
                modalDialog createDashboardModalId "New dashboard" (Some $"Create a dashboard in {folder.Name.Value}.") (createDashboardForm token sid fid)
            if permissions.EditFolder then
                modalDialog renameModalId $"Rename {folder.Name.Value}" None (renameFolderForm token sid fid folder.Name.Value)
        }

    let resourcesPage (token: string) (space: SpaceData) (resources: ResourceData list) (apps: AppData list) (permissions: SpacePermissionsDto) =
        let sid = spaceId space
        section (class' = "stack") {
            spaceTabs space "resources"
            section (class' = "grid grid-two") {
                section (class' = "card") {
                    cardHeader "Resources" (Some "HTTP and PostgreSQL connections")
                    div (class' = "card-list") {
                        for resource in resources do
                            let rid = resourceId resource
                            let usedBy = apps |> List.filter (fun app -> app.ResourceId = resource.Id)
                            article (class' = "list-row") {
                                UiHtml.attrs [ "src", (if resource.ResourceKind = ResourceKind.Http then "/assets/http.svg" else "/assets/postgres.png"); "alt", ""; "class", "resource-icon" ] (img ())
                                div () {
                                    h3 () { resource.Name.Value }
                                    p () { resource.Description.Value }
                                    small () { $"{resource.ResourceKind} · Used by {List.length usedBy} apps" }
                                }
                                if List.isEmpty usedBy && permissions.DeleteResource then
                                    let formTag7 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/resources/{rid}/delete" []
                                    formTag7 {
                                        UiHtml.antiforgeryInput token
                                        UiHtml.dangerSubmitButton "Delete"
                                    }
                            }
                    }
                }

                if permissions.CreateResource then
                    section (class' = "card") {
                        cardHeader "Create HTTP resource" (Some "SQL resources can be created by choosing PostgreSQL below.")
                        let action = $"/_ui/spaces/{sid}/resources/create"
                        let formTag8 = UiHtml.enhancedPostForm action []
                        formTag8 {
                            UiHtml.antiforgeryInput token
                            UiHtml.hidden "Kind" "http"
                            field "Name" (UiHtml.textInput "Name" String.Empty true "Core API") None
                            field "Description" (UiHtml.textInput "Description" String.Empty true "Internal API") None
                            field "Base URL" (UiHtml.textInput "BaseUrl" String.Empty true "https://example.internal") None
                            h3 () { "Default headers" }
                            kvRows "Header" []
                            UiHtml.submitButton "Create HTTP resource"
                        }
                    }
            }
        }

    let appEditor (token: string) (space: SpaceData) (app: AppData) (resource: ResourceData) =
        let sid = spaceId space
        let aid = appId app
        section (class' = "stack") {
            spaceTabs space "builder"
            section (class' = "card") {
                cardHeader app.Name (Some "App editor")
                div (class' = "actions") {
                    a (href = $"/spaces/{sid}/{aid}/run", class' = "button") { "Run app" }
                    a (href = $"/audit?scope=app&appId={aid}", class' = "button button-ghost") { "Audit" }
                }
                div (class' = "form-grid") {
                    let formTag9 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/apps/{aid}/name" []
                    formTag9 {
                        UiHtml.antiforgeryInput token
                        field "Name" (UiHtml.textInput "Name" app.Name true "App name") None
                        UiHtml.submitButton "Rename"
                    }
                    let formTag10 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/apps/{aid}/description" []
                    formTag10 {
                        UiHtml.antiforgeryInput token
                        field "Description" (UiHtml.textInput "Description" (app.Description |> Option.defaultValue String.Empty) false "What this app does") None
                        UiHtml.submitButton "Save description"
                    }
                }
                p () { $"Resource: {resource.Name.Value} ({resource.ResourceKind})" }
            }

            section (class' = "card") {
                cardHeader "Inputs" (Some "Plain fields replace the previous rich placeholder editor. Use @InputName, @\"Input Name\", and {{ expression }} syntax in request templates.")
                div (class' = "table-wrap") {
                    table () {
                        thead () {
                            tr () {
                                th () { "Title" }
                                th () { "Type" }
                                th () { "Required" }
                                th () { "Default" }
                            }
                        }
                        tbody () {
                            for input in app.Inputs do
                                tr () {
                                    td () { input.Title }
                                    td () { UiFormat.inputType input.Type }
                                    td () { if input.Required then "Yes" else "No" }
                                    td () { UiFormat.defaultValue input }
                                }
                        }
                    }
                }
            }

            section (class' = "card") {
                cardHeader "Request configuration" (Some "Edit HTTP method, URL path, SQL/raw templates, headers, parameters, and body as plain text.")
                let formTag11 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/apps/{aid}/config" []
                formTag11 {
                    UiHtml.antiforgeryInput token
                    div (class' = "form-grid") {
                        field "HTTP method" (UiHtml.textInput "HttpMethod" (app.HttpMethod.ToString()) true "GET") None
                        field "URL path" (UiHtml.textInput "UrlPath" (app.UrlPath |> Option.defaultValue String.Empty) false "/customers/@CustomerId") None
                    }
                    label (class' = "field") {
                        span () { "Raw SQL (for SQL apps)" }
                        UiHtml.textareaInput "RawSql" (app.SqlConfig |> Option.bind (fun config -> config.RawSql) |> Option.defaultValue String.Empty) 8
                    }
                    label (class' = "checkbox-field") {
                        UiHtml.checkbox "UseDynamicJsonBody" "true" app.UseDynamicJsonBody
                        span () { "Use dynamic JSON body at run time" }
                    }
                    UiHtml.submitButton "Save configuration"
                }
            }
        }

    let runAppPage (token: string) (space: SpaceData) (app: AppData) (result: RunData option) =
        let sid = spaceId space
        let aid = appId app
        section (class' = "stack") {
            spaceTabs space "builder"
            section (class' = "card") {
                cardHeader $"Run {app.Name}" app.Description
                let formTag12 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/apps/{aid}/run" []
                formTag12 {
                    UiHtml.antiforgeryInput token
                    for appInput in app.Inputs do
                        let inputType = UiFormat.inputHtmlType appInput.Type
                        field appInput.Title (UiHtml.attrs ([ "type", inputType; "name", appInput.Title; "value", UiFormat.defaultValue appInput ] @ UiHtml.requiredAttr appInput.Required) (input ())) appInput.Description
                    if app.UseDynamicJsonBody then
                        h3 () { "Dynamic JSON body" }
                        kvRows "DynamicBody" []
                    UiHtml.submitButton "Run app"
                }
            }

            match result with
            | None -> ()
            | Some run ->
                section (id = "run-result", class' = "card sensitive") {
                    cardHeader "Run result" (Some (UiFormat.runStatus run.Status))
                    match run.ErrorMessage with
                    | Some error -> p (class' = "flash flash-error") { error }
                    | None -> ()
                    match run.ExecutableRequest with
                    | Some request ->
                        h3 () { "Request" }
                        let headerNames = request.Headers |> List.map fst |> String.concat ", "
                        pre (class' = "code-block") { code () { sprintf "%s %s\nHeaders: %s" request.HttpMethod request.BaseUrl headerNames } }
                    | None -> ()
                    match run.ExecutedSql with
                    | Some sql -> pre (class' = "code-block") { code () { sql } }
                    | None -> ()
                    match run.Response with
                    | Some response -> pre (class' = "code-block") { code () { UiFormat.tryFormatJson response } }
                    | None -> p () { "No response body." }
                }
        }

    let dashboardEditor (token: string) (space: SpaceData) (dashboard: DashboardData) (apps: AppData list) =
        let sid = spaceId space
        let did = dashboardId dashboard
        section (class' = "stack") {
            spaceTabs space "builder"
            section (class' = "card") {
                cardHeader dashboard.Name.Value (Some "Dashboard editor")
                div (class' = "actions") {
                    a (href = $"/spaces/{sid}/{did}/dashboard-run", class' = "button") { "Run dashboard" }
                    a (href = $"/audit?scope=dashboard&dashboardId={did}", class' = "button button-ghost") { "Audit" }
                }
                let formTag13 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/dashboards/{did}/name" []
                formTag13 {
                    UiHtml.antiforgeryInput token
                    field "Name" (UiHtml.textInput "Name" dashboard.Name.Value true "Dashboard name") None
                    UiHtml.submitButton "Rename"
                }
            }
            section (class' = "card") {
                cardHeader "Configuration JSON" (Some "Dashboard configuration is stored as JSON compatible with the existing runtime parser.")
                let formTag14 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/dashboards/{did}/config" []
                formTag14 {
                    UiHtml.antiforgeryInput token
                    label (class' = "field") {
                        span () { "Prepare app" }
                        select (name = "PrepareAppId") {
                            option (value = "") { "No prepare app" }
                            for app in apps do
                                UiHtml.optionTag (appId app) app.Name (dashboard.PrepareAppId = Some app.Id)
                        }
                    }
                    label (class' = "field") {
                        span () { "Configuration" }
                        UiHtml.textareaInput "Configuration" (UiFormat.tryFormatJson dashboard.Configuration) 16
                    }
                    UiHtml.submitButton "Save dashboard"
                }
            }
        }

    let dashboardRuntimePage (token: string) (space: SpaceData) (dashboard: DashboardData) (result: string option) =
        let sid = spaceId space
        let did = dashboardId dashboard
        section (class' = "stack") {
            spaceTabs space "builder"
            section (class' = "card") {
                cardHeader $"Run {dashboard.Name.Value}" (Some "Prepare and action execution uses the existing dashboard handlers.")
                let formTag15 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/dashboards/{did}/prepare" []
                formTag15 {
                    UiHtml.antiforgeryInput token
                    p () { "Load input support is intentionally plain text for the server-rendered cutover." }
                    kvRows "LoadInput" []
                    UiHtml.submitButton "Prepare dashboard"
                }
            }
            match result with
            | None -> ()
            | Some text -> section (class' = "card sensitive") { pre (class' = "code-block") { code () { text } } }
        }

    type private PermissionColumn = {
        FormField: string
        ShortLabel: string
        Label: string
        IsChecked: SpacePermissionsDto -> bool
    }

    type private PermissionGroup = {
        Label: string
        Columns: PermissionColumn list
    }

    let private permissionGroups = [
        {
            Label = "Resources"
            Columns = [
                {
                    FormField = "CreateResource"
                    ShortLabel = "Create"
                    Label = "Create resources"
                    IsChecked = fun permissions -> permissions.CreateResource
                }
                {
                    FormField = "EditResource"
                    ShortLabel = "Edit"
                    Label = "Edit resources"
                    IsChecked = fun permissions -> permissions.EditResource
                }
                {
                    FormField = "DeleteResource"
                    ShortLabel = "Delete"
                    Label = "Delete resources"
                    IsChecked = fun permissions -> permissions.DeleteResource
                }
            ]
        }
        {
            Label = "Apps"
            Columns = [
                {
                    FormField = "CreateApp"
                    ShortLabel = "Create"
                    Label = "Create apps"
                    IsChecked = fun permissions -> permissions.CreateApp
                }
                {
                    FormField = "EditApp"
                    ShortLabel = "Edit"
                    Label = "Edit apps"
                    IsChecked = fun permissions -> permissions.EditApp
                }
                {
                    FormField = "DeleteApp"
                    ShortLabel = "Delete"
                    Label = "Delete apps"
                    IsChecked = fun permissions -> permissions.DeleteApp
                }
                {
                    FormField = "RunApp"
                    ShortLabel = "Run"
                    Label = "Run apps"
                    IsChecked = fun permissions -> permissions.RunApp
                }
            ]
        }
        {
            Label = "Folders"
            Columns = [
                {
                    FormField = "CreateFolder"
                    ShortLabel = "Create"
                    Label = "Create folders"
                    IsChecked = fun permissions -> permissions.CreateFolder
                }
                {
                    FormField = "EditFolder"
                    ShortLabel = "Edit"
                    Label = "Edit folders"
                    IsChecked = fun permissions -> permissions.EditFolder
                }
                {
                    FormField = "DeleteFolder"
                    ShortLabel = "Delete"
                    Label = "Delete folders"
                    IsChecked = fun permissions -> permissions.DeleteFolder
                }
            ]
        }
        {
            Label = "Dashboards"
            Columns = [
                {
                    FormField = "CreateDashboard"
                    ShortLabel = "Create"
                    Label = "Create dashboards"
                    IsChecked = fun permissions -> permissions.CreateDashboard
                }
                {
                    FormField = "EditDashboard"
                    ShortLabel = "Edit"
                    Label = "Edit dashboards"
                    IsChecked = fun permissions -> permissions.EditDashboard
                }
                {
                    FormField = "DeleteDashboard"
                    ShortLabel = "Delete"
                    Label = "Delete dashboards"
                    IsChecked = fun permissions -> permissions.DeleteDashboard
                }
                {
                    FormField = "RunDashboard"
                    ShortLabel = "Run"
                    Label = "Run dashboards"
                    IsChecked = fun permissions -> permissions.RunDashboard
                }
            ]
        }
    ]

    let private groupStartClass (baseClass: string) (columnIndex: int) =
        if columnIndex = 0 then $"{baseClass} permission-group-start" else baseClass

    let private sortText (value: string) =
        if isNull value then String.Empty else value.ToLowerInvariant()

    let private memberDisplayName (spaceMember: SpaceMemberPermissionsDto) =
        let hasName =
            not (String.IsNullOrWhiteSpace spaceMember.UserName)
            && not (String.Equals(spaceMember.UserName, "Unknown User", StringComparison.OrdinalIgnoreCase))

        if hasName then
            spaceMember.UserName
        elif not (String.IsNullOrWhiteSpace spaceMember.UserEmail) then
            spaceMember.UserEmail
        else
            "Unknown user"

    let private memberInitials (spaceMember: SpaceMemberPermissionsDto) =
        let source = memberDisplayName spaceMember

        let parts =
            source.Split([| ' '; '.'; '@'; '-' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.truncate 2
            |> Array.choose (fun part ->
                if String.IsNullOrWhiteSpace part then None else Some(part.Substring(0, 1).ToUpperInvariant()))

        if Array.isEmpty parts then "?" else String.concat String.Empty parts

    let private memberAvatar (spaceMember: SpaceMemberPermissionsDto) =
        span (class' = "avatar") {
            match spaceMember.ProfilePicUrl with
            | Some url when not (String.IsNullOrWhiteSpace url) ->
                UiHtml.attrs [ "src", url; "alt", memberDisplayName spaceMember ] (img ())
            | _ -> memberInitials spaceMember
        }

    let private inheritsAllPermissions (spaceMember: SpaceMemberPermissionsDto) =
        spaceMember.IsModerator || spaceMember.IsOrgAdmin

    let private memberPermissionsFormId = "member-permissions-batch-form"

    let private permissionFieldForUser (field: string) (userId: string) = $"{field}:{userId}"

    let private permissionCheckboxInputWithAttrs
        (formId: string option)
        (name: string)
        (labelText: string)
        (isChecked: bool)
        (isDisabled: bool)
        (extraAttrs: (string * string) list)
        =
        let formAttrs =
            formId
            |> Option.map (fun value -> [ "form", value ])
            |> Option.defaultValue []

        UiHtml.attrs
            ([ "type", "checkbox"
               "name", name
               "value", "true"
               "class", "permission-matrix-checkbox"
               "aria-label", labelText ]
             @ formAttrs
             @ extraAttrs
             @ UiHtml.checkedAttr isChecked
             @ UiHtml.disabledAttr isDisabled)
            (input ())

    let private permissionCheckboxInput
        (formId: string option)
        (name: string)
        (labelText: string)
        (isChecked: bool)
        (isDisabled: bool)
        =
        permissionCheckboxInputWithAttrs formId name labelText isChecked isDisabled []

    let private savePermissionsButton (formId: string) =
        let tag =
            UiHtml.attrs
                [ "type", "submit"
                  "form", formId
                  "class", "button"
                  "data-permissions-save", "true" ]
                (button ())

        tag { "Save permissions" }

    let private dangerSubmitButtonSmall (labelText: string) =
        button (type' = "submit", class' = "button button-danger button-small") { labelText }

    let private permissionMatrixHeader (firstColumnLabel: string) (includeActions: bool) =
        thead () {
            tr () {
                let firstHeader = UiHtml.attrs [ "rowspan", "2"; "class", "member-column" ] (th ())
                firstHeader { firstColumnLabel }

                for group in permissionGroups do
                    let groupHeader =
                        UiHtml.attrs
                            [ "colspan", string group.Columns.Length
                              "class", "permission-group-header" ]
                            (th ())

                    groupHeader { group.Label }

                if includeActions then
                    let actionHeader = UiHtml.attrs [ "rowspan", "2"; "class", "actions-column" ] (th ())
                    actionHeader { "Actions" }
            }
            tr () {
                for group in permissionGroups do
                    for columnIndex, column in group.Columns |> List.indexed do
                        let header =
                            UiHtml.attrs
                                [ "class", groupStartClass "permission-column-header" columnIndex
                                  "title", column.Label ]
                                (th ())

                        header { column.ShortLabel }
            }
        }

    let private defaultPermissionsMatrix (permissions: SpacePermissionsDto) =
        div (class' = "table-wrap permissions-matrix-wrap") {
            table (class' = "permissions-table") {
                permissionMatrixHeader "Applies to" false
                tbody () {
                    tr () {
                        td (class' = "member-column") {
                            strong () { "Regular members" }
                            br ()
                            small (class' = "member-email") { "Default access" }
                        }

                        for group in permissionGroups do
                            for columnIndex, column in group.Columns |> List.indexed do
                                td (class' = groupStartClass "permission-cell" columnIndex) {
                                    permissionCheckboxInput
                                        None
                                        column.FormField
                                        $"Default {column.Label.ToLowerInvariant()}"
                                        (column.IsChecked permissions)
                                        false
                                }
                    }
                }
            }
        }

    let settingsPage (token: string) (space: SpaceData) (users: UserData list) (members: SpaceMemberPermissionsDto list) (defaultPermissions: SpacePermissionsDto) =
        let sid = spaceId space

        let sortedMembers =
            members
            |> List.sortBy (fun spaceMember -> (sortText (memberDisplayName spaceMember), sortText spaceMember.UserEmail))

        let editableMembers = sortedMembers |> List.filter (inheritsAllPermissions >> not)

        section (class' = "stack") {
            spaceTabs space "settings"
            section (class' = "grid grid-two") {
                section (class' = "card") {
                    cardHeader "General settings" None
                    let formTag16 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/name" []
                    formTag16 {
                        UiHtml.antiforgeryInput token
                        field "Space name" (UiHtml.textInput "Name" space.Name true "Space name") None
                        UiHtml.submitButton "Rename space"
                    }
                    let formTag17 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/moderator" []
                    formTag17 {
                        UiHtml.antiforgeryInput token
                        label (class' = "field") {
                            span () { "Moderator" }
                            let selectTag3 = UiHtml.attrs [ "name", "NewModeratorUserId"; "required", "required" ] (select ())
                            selectTag3 {
                                for user in users do
                                    UiHtml.optionTag (userId user) ($"{user.Name} ({user.Email})") (user.Id = space.ModeratorUserId)
                            }
                        }
                        UiHtml.submitButton "Change moderator"
                    }
                    let formTag18 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/members/add" []
                    formTag18 {
                        UiHtml.antiforgeryInput token
                        label (class' = "field") {
                            span () { "Add member" }
                            let selectTag4 = UiHtml.attrs [ "name", "UserId"; "required", "required" ] (select ())
                            selectTag4 {
                                option (value = "") { "Select user" }
                                for user in users do
                                    option (value = userId user) { $"{user.Name} ({user.Email})" }
                            }
                        }
                        UiHtml.submitButton "Add member"
                    }
                }

                section (class' = "card danger-zone") {
                    cardHeader "Danger zone" (Some "Deleting a space also removes authorization relationships.")
                    let formTag19 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/delete" [ "data-confirm", $"Delete space {space.Name}?" ]
                    formTag19 {
                        UiHtml.antiforgeryInput token
                        UiHtml.dangerSubmitButton "Delete space"
                    }
                }
            }

            let membersSection = UiHtml.attrs [ "class", "card"; "data-permissions-matrix", "true" ] (section ())
            membersSection {
                div (class' = "card-header card-header-actions") {
                    div () {
                        h2 () { "Members" }
                        p () { "Permissions are shown as a matrix so you can scan access by user and capability. Moderators and organization admins inherit every permission." }
                    }

                    if not (List.isEmpty editableMembers) then
                        savePermissionsButton memberPermissionsFormId
                }

                if List.isEmpty sortedMembers then
                    emptyState "No members" "Add members to this space before assigning permissions."
                else
                    if not (List.isEmpty editableMembers) then
                        let formTag20 =
                            UiHtml.enhancedPostForm
                                $"/_ui/spaces/{sid}/members/permissions"
                                [ "id", memberPermissionsFormId
                                  "class", "detached-form" ]

                        formTag20 {
                            UiHtml.antiforgeryInput token

                            for spaceMember in editableMembers do
                                UiHtml.hidden "UserIds" spaceMember.UserId
                        }

                    div (class' = "permissions-matrix") {
                        div (class' = "table-wrap permissions-matrix-wrap") {
                                table (class' = "permissions-table") {
                                    permissionMatrixHeader "User" true
                                    tbody () {
                                        for spaceMember in sortedMembers do
                                            let isInherited = inheritsAllPermissions spaceMember
                                            let displayName = memberDisplayName spaceMember
                                            let rowClass = if isInherited then "permission-row permission-row-inherited" else "permission-row"

                                            tr (class' = rowClass) {
                                                td (class' = "member-column") {
                                                    div (class' = "member-summary") {
                                                        memberAvatar spaceMember
                                                        div (class' = "member-summary-text") {
                                                            span (class' = "member-name-line") {
                                                                strong () { displayName }

                                                                if spaceMember.IsModerator then
                                                                    let crown =
                                                                        UiHtml.attrs
                                                                            [ "class", "moderator-crown"
                                                                              "title", "Space moderator"
                                                                              "aria-label", "Space moderator" ]
                                                                            (span ())

                                                                    crown { "👑" }
                                                            }
                                                        }
                                                    }
                                                }

                                                for group in permissionGroups do
                                                    for columnIndex, column in group.Columns |> List.indexed do
                                                        let isChecked = isInherited || column.IsChecked spaceMember.Permissions
                                                        let checkboxName = permissionFieldForUser column.FormField spaceMember.UserId

                                                        td (class' = groupStartClass "permission-cell" columnIndex) {
                                                            permissionCheckboxInputWithAttrs
                                                                (if isInherited then None else Some memberPermissionsFormId)
                                                                checkboxName
                                                                $"{column.Label} for {displayName}"
                                                                isChecked
                                                                isInherited
                                                                (if isInherited then
                                                                     []
                                                                 else
                                                                     [ "data-permission-checkbox", "true"
                                                                       "data-initial-checked", if isChecked then "true" else "false" ])
                                                        }

                                                td (class' = "actions-column") {
                                                    if isInherited then
                                                        span (class' = "badge") { "Inherited" }
                                                    else
                                                        let formTag21 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/members/remove" []
                                                        formTag21 {
                                                            UiHtml.antiforgeryInput token
                                                            UiHtml.hidden "UserId" spaceMember.UserId
                                                            dangerSubmitButtonSmall "Remove"
                                                        }
                                                }
                                            }
                                    }
                                }
                            }

                        div (class' = "permissions-legend") {
                            span () { "✓ Moderators and organization admins inherit every permission." }
                            span () { "Change any checkbox to enable Save permissions." }
                        }
                    }
            }

            section (class' = "card") {
                cardHeader "Default member permissions" (Some "Applied to regular members through OpenFGA inherited relationships.")
                let formTag22 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/default-member-permissions" []
                formTag22 {
                    UiHtml.antiforgeryInput token
                    defaultPermissionsMatrix defaultPermissions
                    UiHtml.submitButton "Save default permissions"
                }
            }
        }

    let usersPage (users: UserData list) (spaces: SpaceData list) (currentUserId: UserId option) =
        section (class' = "card") {
            cardHeader "Users" (Some $"{List.length users} users")
            div (class' = "table-wrap") {
                table () {
                    thead () {
                        tr () {
                            th () { "User" }
                            th () { "Status" }
                            th () { "Spaces" }
                            th () { "Audit" }
                        }
                    }
                    tbody () {
                        for user in users do
                            let memberships = spaces |> List.filter (fun space -> space.ModeratorUserId = user.Id || List.contains user.Id space.MemberIds)
                            tr () {
                                td () {
                                    div (class' = "user-cell") {
                                        userAvatar user
                                        div () {
                                            strong () { displayUserName user }
                                            if Some user.Id = currentUserId then span (class' = "badge") { "You" }
                                            br ()
                                            small () { user.Email }
                                        }
                                    }
                                }
                                td () { if user.InvitedAt.IsSome then "Pending invite" else "Active" }
                                td () { memberships |> List.map (fun space -> space.Name) |> String.concat ", " }
                                td () { a (href = $"/audit?scope=user&userId={userId user}", class' = "button button-ghost") { "Audit log" } }
                            }
                    }
                }
            }
        }

    let auditPage (events: EnhancedEventData list) (page: int) (total: int) (scope: string option) (baseHref: string) =
        let totalPages = max 1 ((total + UiModels.PageSize - 1) / UiModels.PageSize)
        let pageHref targetPage =
            let separator = if baseHref.Contains "?" then "&" else "?"
            $"{baseHref}{separator}page={targetPage}"

        section (class' = "stack") {
            section (class' = "card") {
                cardHeader "Audit log" (Some $"{total} events · Page {page} of {totalPages}")
                p () { scope |> Option.defaultValue "Showing all audit events." }
            }
            for event in events do
                let entityName = if String.IsNullOrWhiteSpace event.EntityName then event.EntityId else event.EntityName

                article (class' = "card audit-card") {
                    div (class' = "audit-meta") {
                        span (class' = "badge") { UiFormat.eventType event.EventType }
                        span (class' = "badge badge-muted") { UiFormat.entityType event.EntityType }
                        span () { UiFormat.dateTime event.OccurredAt }
                    }
                    h3 () { event.EventSummary }
                    p () { $"{entityName} · Event {event.EventId.Substring(0, Math.Min(8, event.EventId.Length))} by {event.UserName}" }
                    details () {
                        summary () { "Raw event data" }
                        pre (class' = "code-block") { code () { UiFormat.tryFormatJson event.EventData } }
                    }
                }
            div (class' = "pagination") {
                span (class' = "pagination-status") { $"Page {page} of {totalPages}" }
                if page > 1 then a (href = pageHref (page - 1), class' = "button button-secondary") { "Previous" }
                if page < totalPages then a (href = pageHref (page + 1), class' = "button button-secondary") { "Next" }
            }
        }

    let trashPage (token: string) (space: SpaceData) (apps: ValidatedApp list) (folders: ValidatedFolder list) (resources: ValidatedResource list) =
        let sid = spaceId space
        section (class' = "stack") {
            spaceTabs space "trash"
            section (class' = "card") {
                cardHeader "Trash" (Some "Restore deleted apps, folders, and resources.")
                if List.isEmpty apps && List.isEmpty folders && List.isEmpty resources then
                    emptyState "Trash is empty" "Deleted items for this space will appear here."
                else
                    div (class' = "card-list") {
                        for app in apps do
                            article (class' = "list-row") {
                                div () {
                                    h3 () { app.State.Name }
                                    p () { "App" }
                                }
                                let formTag21 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/trash/apps/{app.State.Id.Value}/restore" []
                                formTag21 {
                                    UiHtml.antiforgeryInput token
                                    UiHtml.submitButton "Restore"
                                }
                            }
                        for folder in folders do
                            article (class' = "list-row") {
                                div () {
                                    h3 () { folder.State.Name.Value }
                                    p () { "Folder" }
                                }
                                let formTag22 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/trash/folders/{folder.State.Id.Value}/restore" []
                                formTag22 {
                                    UiHtml.antiforgeryInput token
                                    UiHtml.submitButton "Restore"
                                }
                            }
                        for resource in resources do
                            article (class' = "list-row") {
                                div () {
                                    h3 () { resource.State.Name.Value }
                                    p () { "Resource" }
                                }
                                let formTag23 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/trash/resources/{resource.State.Id.Value}/restore" []
                                formTag23 {
                                    UiHtml.antiforgeryInput token
                                    UiHtml.submitButton "Restore"
                                }
                            }
                    }
            }
        }

    let devSelectUser (token: string) (users: UserData list) (returnUrl: string) (error: string option) =
        section (class' = "dev-select-page") {
            section (class' = "card") {
                cardHeader "Choose development user" (Some "Server-rendered pages use a local development cookie instead of React localStorage headers.")
                match error with
                | Some message -> p (class' = "flash flash-error") { message }
                | None -> ()
                form (method = "post", action = "/dev/select-user") {
                    UiHtml.antiforgeryInput token
                    UiHtml.hidden "returnUrl" returnUrl
                    label (class' = "field") {
                        span () { "User" }
                        let selectTag5 = UiHtml.attrs [ "name", "userId"; "required", "required" ] (select ())
                        selectTag5 {
                            option (value = "") { "Select user" }
                            for user in users do
                                option (value = userId user) { $"{user.Name} ({user.Email})" }
                        }
                    }
                    UiHtml.submitButton "Continue"
                }
            }
        }
