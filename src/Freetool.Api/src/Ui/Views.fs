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

    let private iconSvg (iconName: string) : HtmlElement =
        let normalized =
            match iconName with
            | "+" -> "plus"
            | value -> value

        let body =
            match normalized with
            | "plus" -> "<path d=\"M5 12h14\"></path><path d=\"M12 5v14\"></path>"
            | "edit" ->
                "<path d=\"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z\"></path><path d=\"m15 5 4 4\"></path>"
            | "trash" ->
                "<path d=\"M3 6h18\"></path><path d=\"M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6\"></path><path d=\"M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2\"></path><path d=\"M10 11v6\"></path><path d=\"M14 11v6\"></path>"
            | "play" -> "<polygon points=\"6 3 20 12 6 21 6 3\"></polygon>"
            | "settings" ->
                "<path d=\"M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z\"></path><circle cx=\"12\" cy=\"12\" r=\"3\"></circle>"
            | "folder" ->
                "<path d=\"M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z\"></path>"
            | "app" ->
                "<rect width=\"20\" height=\"16\" x=\"2\" y=\"4\" rx=\"2\"></rect><path d=\"M10 4v4\"></path><path d=\"M2 8h20\"></path>"
            | "dashboard" ->
                "<rect width=\"7\" height=\"9\" x=\"3\" y=\"3\" rx=\"1\"></rect><rect width=\"7\" height=\"5\" x=\"14\" y=\"3\" rx=\"1\"></rect><rect width=\"7\" height=\"9\" x=\"14\" y=\"12\" rx=\"1\"></rect><rect width=\"7\" height=\"5\" x=\"3\" y=\"16\" rx=\"1\"></rect>"
            | "resource" ->
                "<path d=\"M12 22v-5\"></path><path d=\"M9 8V2\"></path><path d=\"M15 8V2\"></path><path d=\"M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z\"></path>"
            | "dot" -> "<circle cx=\"12\" cy=\"12\" r=\"1\"></circle>"
            | _ -> normalized

        if body = normalized then
            span (class' = "ui-icon-fallback") { normalized }
        else
            raw
                $"<svg class=\"ui-icon\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{body}</svg>"

    let private iconOnlyButton (labelText: string) (iconName: string) (extraAttrs: (string * string) list) : HtmlElement =
        let tag =
            UiHtml.attrs
                ([ "type", "button"
                   "class", "icon-button"
                   "aria-label", labelText
                   "title", labelText ]
                 @ extraAttrs)
                (button ())

        tag {
            span (class' = "button-icon") { iconSvg iconName }
            srOnly labelText
        }

    let private iconOnlyLink (href: string) (labelText: string) (iconName: string) : HtmlElement =
        let tag =
            UiHtml.attrs
                [ "href", href
                  "class", "icon-button"
                  "aria-label", labelText
                  "title", labelText ]
                (a ())

        tag {
            span (class' = "button-icon") { iconSvg iconName }
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
                span (class' = "button-icon") { iconSvg "trash" }
                srOnly labelText
            }
        }

    let private modalOpenButton (modalId: string) (labelText: string) (iconName: string) (buttonClass: string) : HtmlElement =
        let tag =
            UiHtml.attrs
                [ "type", "button"
                  "class", buttonClass
                  "data-modal-open", modalId
                  "aria-label", labelText ]
                (button ())

        tag {
            span (class' = "button-icon") { iconSvg iconName }
            span () { labelText }
        }

    let private iconModalOpenButton (modalId: string) (labelText: string) (iconName: string) : HtmlElement =
        iconOnlyButton labelText iconName [ "data-modal-open", modalId ]

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

    let private keyValuePairDto (pair: KeyValuePair) : KeyValuePairDto = { Key = pair.Key; Value = pair.Value }

    let private kvRows (namePrefix: string) (pairs: KeyValuePairDto list) =
        let container = UiHtml.attrs [ "class", "kv-rows"; "data-kv-rows", "true" ] (div ())

        container {
            for _, pair in pairs |> List.indexed do
                div (class' = "kv-row") {
                    UiHtml.textInput $"{namePrefix}Key" pair.Key false "Key"
                    UiHtml.textInputWithAttrs
                        $"{namePrefix}Value"
                        pair.Value
                        false
                        "Value or @InputName"
                        [ "data-template-input", "true" ]

                    let removeButton =
                        UiHtml.attrs [ "type", "button"; "class", "button button-ghost"; "data-remove-row", "true" ] (button ())

                    removeButton { "Remove" }
                }

            div (class' = "kv-row kv-row-template") {
                UiHtml.textInput $"{namePrefix}Key" String.Empty false "Key"
                UiHtml.textInputWithAttrs
                    $"{namePrefix}Value"
                    String.Empty
                    false
                    "Value or @InputName"
                    [ "data-template-input", "true" ]

                let removeButton =
                    UiHtml.attrs [ "type", "button"; "class", "button button-ghost"; "data-remove-row", "true" ] (button ())

                removeButton { "Remove" }
            }

            let addButton =
                UiHtml.attrs [ "type", "button"; "class", "button button-secondary"; "data-add-kv-row", "true" ] (button ())

            addButton { "Add row" }
        }

    let private csv (values: string list) = String.Join(", ", values)

    let private inputTypeFormValue (inputType: InputType) =
        match inputType.Value with
        | InputTypeValue.Email -> "email"
        | InputTypeValue.Date -> "date"
        | InputTypeValue.Text _ -> "text"
        | InputTypeValue.Integer -> "integer"
        | InputTypeValue.Boolean -> "boolean"
        | InputTypeValue.Currency SupportedCurrency.USD -> "currency"
        | InputTypeValue.MultiEmail _ -> "multi-email"
        | InputTypeValue.MultiDate _ -> "multi-date"
        | InputTypeValue.MultiText _ -> "multi-text"
        | InputTypeValue.MultiInteger _ -> "multi-integer"
        | InputTypeValue.Radio _ -> "radio"

    let private inputTypeConfigValue (inputType: InputType) =
        match inputType.Value with
        | InputTypeValue.Text maxLength -> string maxLength
        | InputTypeValue.MultiEmail emails -> emails |> List.map string |> csv
        | InputTypeValue.MultiDate dates -> dates |> List.map (fun date -> date.ToString("yyyy-MM-dd")) |> csv
        | InputTypeValue.MultiText(maxLength, allowedValues) -> $"{maxLength}|{csv allowedValues}"
        | InputTypeValue.MultiInteger values -> values |> List.map string |> csv
        | InputTypeValue.Radio options -> options |> List.map (fun option -> option.Value) |> csv
        | _ -> String.Empty

    let private inputTypeOption (selectedValue: string) (value: string) (labelText: string) =
        let tag = UiHtml.attrs ([ "value", value ] @ UiHtml.selectedAttr (selectedValue = value)) (option ())
        tag { labelText }

    let private inputRows (inputs: Input list) =
        let row (input: Input option) (template: bool) =
            let title = input |> Option.map (fun value -> value.Title) |> Option.defaultValue String.Empty
            let description = input |> Option.bind (fun value -> value.Description) |> Option.defaultValue String.Empty
            let selectedType = input |> Option.map (fun value -> inputTypeFormValue value.Type) |> Option.defaultValue "text"
            let requiredValue = input |> Option.map (fun value -> if value.Required then "true" else "false") |> Option.defaultValue "false"
            let defaultValue = input |> Option.map UiFormat.defaultValue |> Option.defaultValue String.Empty
            let typeConfig = input |> Option.map (fun value -> inputTypeConfigValue value.Type) |> Option.defaultValue String.Empty
            let rowClass = if template then "input-row input-row-template" else "input-row"

            div (class' = rowClass) {
                UiHtml.textInput "InputTitle" title false "Input title"
                UiHtml.textInput "InputDescription" description false "Description"

                let typeSelect = UiHtml.attrs [ "name", "InputType"; "aria-label", "Input type" ] (select ())

                typeSelect {
                    inputTypeOption selectedType "text" "Text"
                    inputTypeOption selectedType "email" "Email"
                    inputTypeOption selectedType "date" "Date"
                    inputTypeOption selectedType "integer" "Integer"
                    inputTypeOption selectedType "boolean" "Boolean"
                    inputTypeOption selectedType "currency" "Currency (USD)"
                    inputTypeOption selectedType "radio" "Radio"
                    inputTypeOption selectedType "multi-email" "Email choices"
                    inputTypeOption selectedType "multi-date" "Date choices"
                    inputTypeOption selectedType "multi-text" "Text choices"
                    inputTypeOption selectedType "multi-integer" "Integer choices"
                }

                let requiredSelect = UiHtml.attrs [ "name", "InputRequired"; "aria-label", "Required" ] (select ())

                requiredSelect {
                    UiHtml.optionTag "false" "Optional" (requiredValue = "false")
                    UiHtml.optionTag "true" "Required" (requiredValue = "true")
                }

                UiHtml.textInput "InputDefaultValue" defaultValue false "Default value"
                UiHtml.textInput "InputTypeConfig" typeConfig false "Max length or comma options"

                let removeButton =
                    UiHtml.attrs [ "type", "button"; "class", "button button-ghost"; "data-remove-row", "true" ] (button ())

                removeButton { "Remove" }
            }

        let container = UiHtml.attrs [ "class", "input-rows"; "data-input-rows", "true" ] (div ())

        container {
            for input in inputs do
                row (Some input) false

            row None true

            let addButton =
                UiHtml.attrs [ "type", "button"; "class", "button button-secondary"; "data-add-input-row", "true" ] (button ())

            addButton { "Add input" }
        }

    let private methodSelect (selectedMethod: string) =
        let selectTag = UiHtml.attrs [ "name", "HttpMethod"; "required", "required" ] (select ())

        selectTag {
            for method in [ "GET"; "POST"; "PUT"; "PATCH"; "DELETE" ] do
                UiHtml.optionTag method method (String.Equals(selectedMethod, method, StringComparison.OrdinalIgnoreCase))
        }

    let private resourceKindValue (kind: ResourceKind) =
        match kind with
        | ResourceKind.Sql -> "sql"
        | ResourceKind.Http -> "http"

    let private appResourceSection (kind: ResourceKind) (currentKind: ResourceKind option) =
        let kindValue = resourceKindValue kind

        let hiddenAttrs =
            match currentKind with
            | Some selected when selected <> kind -> [ "hidden", "hidden" ]
            | _ -> []

        UiHtml.attrs ([ "class", "app-resource-section"; "data-resource-kind-section", kindValue ] @ hiddenAttrs) (section ())

    let private templateHelpText =
        "Template values can reference app inputs with @InputName or @\"Input Name\" and expressions with {{ ... }}."

    let private appConfigurationFields (resourceKind: ResourceKind option) (inputs: Input list) (app: AppData option) =
        let httpMethod = app |> Option.map (fun value -> value.HttpMethod.ToString()) |> Option.defaultValue "GET"
        let urlPath = app |> Option.bind (fun value -> value.UrlPath) |> Option.defaultValue String.Empty
        let urlParameters = app |> Option.map (fun value -> value.UrlParameters |> List.map keyValuePairDto) |> Option.defaultValue []
        let headers = app |> Option.map (fun value -> value.Headers |> List.map keyValuePairDto) |> Option.defaultValue []
        let body = app |> Option.map (fun value -> value.Body |> List.map keyValuePairDto) |> Option.defaultValue []
        let useDynamicJsonBody = app |> Option.map (fun value -> value.UseDynamicJsonBody) |> Option.defaultValue false
        let rawSql = app |> Option.bind (fun value -> value.SqlConfig) |> Option.bind (fun config -> config.RawSql) |> Option.defaultValue "select 1"

        div (class' = "app-builder-fields") {
            section (class' = "form-section") {
                h3 () { "Inputs" }
                p (class' = "muted") { templateHelpText }
                p (class' = "muted") { "Type config is optional: use a max length for text, comma-separated options for radio/choices, or max|option1, option2 for text choices." }
                inputRows inputs
            }

            let httpSection = appResourceSection ResourceKind.Http resourceKind

            httpSection {
                h3 () { "HTTP request" }
                div (class' = "form-grid") {
                    field "HTTP method" (methodSelect httpMethod) None
                    field
                        "URL path"
                        (UiHtml.textInputWithAttrs "UrlPath" urlPath false "/customers/@CustomerId" [ "data-template-input", "true" ])
                        None
                }

                div (class' = "form-section") {
                    h3 () { "Query parameters" }
                    p (class' = "muted") { templateHelpText }
                    kvRows "UrlParameter" urlParameters
                }

                div (class' = "form-section") {
                    h3 () { "Headers" }
                    p (class' = "muted") { templateHelpText }
                    kvRows "Header" headers
                }

                div (class' = "form-section") {
                    h3 () { "JSON body" }
                    label (class' = "checkbox-field") {
                        UiHtml.attrs ([ "type", "checkbox"; "name", "UseDynamicJsonBody"; "value", "true"; "data-dynamic-body-checkbox", "true" ] @ UiHtml.checkedAttr useDynamicJsonBody) (input ())
                        span () { "Collect JSON key/value pairs at run time" }
                    }

                    let staticBodyAttrs =
                        [ "data-static-body-section", "true" ]
                        @ if useDynamicJsonBody then [ "hidden", "hidden" ] else []

                    let staticBodySection = UiHtml.attrs staticBodyAttrs (div ())

                    staticBodySection { kvRows "Body" body }

                    let dynamicHelpAttrs =
                        [ "class", "muted"; "data-dynamic-body-help", "true" ]
                        @ if useDynamicJsonBody then [] else [ "hidden", "hidden" ]

                    let dynamicHelp = UiHtml.attrs dynamicHelpAttrs (p ())
                    dynamicHelp { "Users will provide JSON body key/value pairs when running this app." }
                }
            }

            let sqlSection = appResourceSection ResourceKind.Sql resourceKind

            sqlSection {
                h3 () { "SQL request" }
                label (class' = "field") {
                    span () { "Raw SQL" }
                    UiHtml.textareaInputWithAttrs "RawSql" rawSql 10 [ "data-template-input", "true" ]
                    small () { templateHelpText }
                }
            }
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
        | "Folder" -> "folder"
        | "App" -> "app"
        | "Dashboard" -> "dashboard"
        | "Resource" -> "resource"
        | _ -> "dot"

    let private objectCard
        (title: string)
        (kind: string)
        (href: string)
        (description: string option)
        (actions: HtmlElement list)
        =
        article (class' = "object-card") {
            a (href = href, class' = "object-card-link", title = kind) {
                span (class' = "object-icon") { iconSvg (objectIcon kind) }
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
        let formTag = UiHtml.enhancedPostForm action [ "data-app-config-form", "true" ]

        formTag {
            UiHtml.antiforgeryInput token
            UiHtml.hidden "FolderId" fid
            field "App name" (UiHtml.textInput "Name" String.Empty true "Customer lookup") None
            field "Description" (UiHtml.textInput "Description" String.Empty false "What this app does") None
            label (class' = "field") {
                span () { "Resource" }

                let selectTag =
                    UiHtml.attrs
                        [ "name", "ResourceId"
                          "required", "required"
                          "data-app-resource-select", "true" ]
                        (select ())

                selectTag {
                    option (value = "") { "Select resource" }
                    for resource in resources do
                        let optionTag =
                            UiHtml.attrs
                                [ "value", resourceId resource
                                  "data-resource-kind", resourceKindValue resource.ResourceKind ]
                                (option ())

                        optionTag { resource.Name.Value }
                }
            }
            appConfigurationFields None [] None
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
                div (class' = "actions") { modalOpenButton modalId "New Space" "plus" "button" }
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
                    modalOpenButton modalId "New Space" "plus" "button"
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
                                iconOnlyLink $"/spaces/{sid}/settings" $"{space.Name} settings" "settings"
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
                            modalOpenButton createFolderModalId "New Folder" "plus" "button"
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
                                iconModalOpenButton subfolderModalId "New subfolder" "plus"
                            if permissions.EditFolder then
                                iconModalOpenButton renameModalId "Rename folder" "edit"
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
                            if permissions.RunApp then iconOnlyLink $"/spaces/{sid}/{aid}/run" "Run app" "play"
                            if permissions.EditApp then iconOnlyLink $"/spaces/{sid}/{aid}" "Edit app" "edit"
                            if permissions.DeleteApp then
                                iconDeleteForm token $"/_ui/spaces/{sid}/apps/{aid}/delete" "Delete app" $"Delete app {app.Name}?"
                        ]

                    objectCard app.Name "App" $"/spaces/{sid}/{aid}" app.Description actions
                for dashboard in dashboards do
                    let did = dashboardId dashboard
                    let actions =
                        [
                            if permissions.RunDashboard then iconOnlyLink $"/spaces/{sid}/{did}/dashboard-run" "Run dashboard" "play"
                            if permissions.EditDashboard then iconOnlyLink $"/spaces/{sid}/{did}" "Edit dashboard" "edit"
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
                            modalOpenButton createFolderModalId "New Folder" "plus" "button button-secondary"
                        if permissions.CreateApp && not (List.isEmpty resources) then
                            modalOpenButton createAppModalId "New App" "plus" "button button-secondary"
                        if permissions.CreateDashboard then
                            modalOpenButton createDashboardModalId "New Dashboard" "plus" "button button-secondary"
                        if permissions.EditFolder then
                            modalOpenButton renameModalId "Rename" "edit" "button button-ghost"
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
                                iconModalOpenButton childSubfolderModalId "New subfolder" "plus"
                            if permissions.EditFolder then
                                iconModalOpenButton childRenameModalId "Rename folder" "edit"
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
                            if permissions.RunApp then iconOnlyLink $"/spaces/{sid}/{aid}/run" "Run app" "play"
                            if permissions.EditApp then iconOnlyLink $"/spaces/{sid}/{aid}" "Edit app" "edit"
                            if permissions.DeleteApp then
                                iconDeleteForm token $"/_ui/spaces/{sid}/apps/{aid}/delete" "Delete app" $"Delete app {app.Name}?"
                        ]

                    objectCard app.Name "App" $"/spaces/{sid}/{aid}" app.Description actions
                for dashboard in dashboards do
                    let did = dashboardId dashboard
                    let actions =
                        [
                            if permissions.RunDashboard then iconOnlyLink $"/spaces/{sid}/{did}/dashboard-run" "Run dashboard" "play"
                            if permissions.EditDashboard then iconOnlyLink $"/spaces/{sid}/{did}" "Edit dashboard" "edit"
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
                cardHeader "Build configuration" (Some "Edit inputs and request templates. Use @InputName, @\"Input Name\", and {{ expression }} syntax in values.")
                let formTag11 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/apps/{aid}/config" [ "data-app-config-form", "true" ]
                formTag11 {
                    UiHtml.antiforgeryInput token
                    appConfigurationFields (Some resource.ResourceKind) app.Inputs (Some app)
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

    let private moderatorInheritedHelp =
        "Moderators get all permissions by default and cannot be removed without changing the moderator."

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

    let private saveChangedPermissionsButton (formId: string) (labelText: string) =
        let tag =
            UiHtml.attrs
                [ "type", "submit"
                  "form", formId
                  "class", "button"
                  "disabled", "disabled"
                  "data-permissions-save", "true" ]
                (button ())

        tag { labelText }

    let private savePermissionsButton (formId: string) =
        saveChangedPermissionsButton formId "Save permissions"

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
                                let isChecked = column.IsChecked permissions

                                td (class' = groupStartClass "permission-cell" columnIndex) {
                                    permissionCheckboxInputWithAttrs
                                        None
                                        column.FormField
                                        $"Default {column.Label.ToLowerInvariant()}"
                                        isChecked
                                        false
                                        [ "data-permission-checkbox", "true"
                                          "data-initial-checked", if isChecked then "true" else "false" ]
                                }
                    }
                }
            }
        }

    let private deleteSpaceConfirmationModal (token: string) (space: SpaceData) =
        let sid = spaceId space
        let modalId = $"delete-space-{sid}"
        let formTag = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/delete" [ "data-typed-confirm-form", "true" ]

        modalDialog
            modalId
            "Delete space"
            (Some "This permanently deletes the space and removes its authorization relationships.")
            (formTag {
                UiHtml.antiforgeryInput token
                p () {
                    "Type "
                    strong () { space.Name }
                    " to confirm deletion."
                }

                let confirmInput =
                    UiHtml.attrs
                        [ "type", "text"
                          "name", "ConfirmName"
                          "placeholder", space.Name
                          "required", "required"
                          "autocomplete", "off"
                          "data-confirm-input", "true"
                          "data-confirm-expected", space.Name ]
                        (input ())

                field "Confirm space name" confirmInput None

                let deleteButton =
                    UiHtml.attrs
                        [ "type", "submit"
                          "class", "button button-danger"
                          "disabled", "disabled"
                          "data-confirm-submit", "true" ]
                        (button ())

                deleteButton { "Delete space" }
            })

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
                    let deleteModalId = $"delete-space-{sid}"
                    modalOpenButton deleteModalId "Delete space" "trash" "button button-danger"
                    deleteSpaceConfirmationModal token space
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

                                                        let checkboxAttrs =
                                                            if spaceMember.IsModerator then
                                                                [ "title", moderatorInheritedHelp ]
                                                            elif isInherited then
                                                                []
                                                            else
                                                                [ "data-permission-checkbox", "true"
                                                                  "data-initial-checked", if isChecked then "true" else "false" ]

                                                        let cellAttrs =
                                                            [ "class", groupStartClass "permission-cell" columnIndex ]
                                                            @ if spaceMember.IsModerator then [ "title", moderatorInheritedHelp ] else []

                                                        let cell = UiHtml.attrs cellAttrs (td ())
                                                        cell {
                                                            permissionCheckboxInputWithAttrs
                                                                (if isInherited then None else Some memberPermissionsFormId)
                                                                checkboxName
                                                                $"{column.Label} for {displayName}"
                                                                isChecked
                                                                isInherited
                                                                checkboxAttrs
                                                        }

                                                let actionsAttrs =
                                                    [ "class", "actions-column" ]
                                                    @ if spaceMember.IsModerator then [ "title", moderatorInheritedHelp ] else []

                                                let actionsCell = UiHtml.attrs actionsAttrs (td ())
                                                actionsCell {
                                                    if isInherited then
                                                        let inheritedAttrs =
                                                            [ "class", "badge" ]
                                                            @ if spaceMember.IsModerator then [ "title", moderatorInheritedHelp ] else []

                                                        let inheritedBadge = UiHtml.attrs inheritedAttrs (span ())
                                                        inheritedBadge { "Inherited" }
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

            let defaultPermissionsSection = UiHtml.attrs [ "class", "card"; "data-permissions-matrix", "true" ] (section ())
            defaultPermissionsSection {
                cardHeader "Default member permissions" (Some "Applied to regular members through OpenFGA inherited relationships.")
                let defaultPermissionsFormId = "default-member-permissions-form"
                let formTag22 =
                    UiHtml.enhancedPostForm
                        $"/_ui/spaces/{sid}/default-member-permissions"
                        [ "id", defaultPermissionsFormId
                          "class", "permissions-form" ]

                formTag22 {
                    UiHtml.antiforgeryInput token
                    defaultPermissionsMatrix defaultPermissions
                    saveChangedPermissionsButton defaultPermissionsFormId "Save default permissions"
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
