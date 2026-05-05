namespace Freetool.Api.Ui

open System
open System.Text.Json
open Oxpecker.ViewEngine
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Services

module Views =
    type AuditFilterValues = {
        Scope: string option
        AppId: string option
        UserId: string option
        DashboardId: string option
        Search: string option
        ActorUserId: string option
        EventType: string option
        EntityType: string option
        FromDate: string option
        ToDate: string option
        IncludeRunEvents: bool
    }

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

    let private headerKeyValuePairDto (pair: KeyValuePair) : KeyValuePairDto =
        if AuthorizationHeaderRedaction.isAuthorizationHeaderKey pair.Key then
            {
                Key = pair.Key
                Value = AuthorizationHeaderRedaction.redactAuthorizationHeaderValue pair.Value
            }
        else
            keyValuePairDto pair

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
        let row (appInput: Input option) (template: bool) =
            let title = appInput |> Option.map (fun value -> value.Title) |> Option.defaultValue String.Empty
            let description = appInput |> Option.bind (fun value -> value.Description) |> Option.defaultValue String.Empty
            let selectedType = appInput |> Option.map (fun value -> inputTypeFormValue value.Type) |> Option.defaultValue "text"
            let requiredValue = appInput |> Option.map (fun value -> if value.Required then "true" else "false") |> Option.defaultValue "false"
            let defaultValue = appInput |> Option.map UiFormat.defaultValue |> Option.defaultValue String.Empty
            let typeConfig = appInput |> Option.map (fun value -> inputTypeConfigValue value.Type) |> Option.defaultValue String.Empty
            let rowClass = if template then "input-row input-row-template" else "input-row"
            let isBoolean = selectedType = "boolean"
            let isRequired = isBoolean || requiredValue = "true"
            let requiredFormValue = if isRequired then "true" else "false"

            div (class' = rowClass) {
                div (class' = "input-row-field input-row-title") {
                    UiHtml.textInput "InputTitle" title false "Field label"
                }

                div (class' = "input-row-field input-row-type") {
                    let typeSelect =
                        UiHtml.attrs [ "name", "InputType"; "aria-label", "Input type"; "data-input-type-select", "true" ] (select ())

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
                }

                div (class' = "input-row-field input-row-required") {
                    let requiredHidden =
                        UiHtml.attrs [ "type", "hidden"; "name", "InputRequired"; "value", requiredFormValue; "data-input-required-value", "true" ] (input ())

                    requiredHidden

                    label (class' = "input-required-switch") {
                        let requiredToggle =
                            UiHtml.attrs
                                ([ "type", "checkbox"; "value", "true"; "aria-label", "Required"; "data-input-required-toggle", "true" ]
                                 @ UiHtml.checkedAttr isRequired
                                 @ UiHtml.disabledAttr isBoolean)
                                (input ())

                        requiredToggle
                        span (class' = "input-switch-track") { span (class' = "input-switch-thumb") { } }

                        let requiredLabel =
                            UiHtml.attrs [ "class", "input-required-label"; "data-input-required-label", "true" ] (span ())

                        requiredLabel {
                            if isBoolean then
                                "Required (boolean)"
                            else
                                "Required"
                        }
                    }
                }

                div (class' = "input-row-field input-row-remove") {
                    let removeButton =
                        UiHtml.attrs
                            [ "type", "button"; "class", "button button-ghost input-row-remove-button"; "data-remove-row", "true" ]
                            (button ())

                    removeButton { "Remove" }
                }

                div (class' = "input-row-field input-row-description") {
                    UiHtml.textInput "InputDescription" description false "Description (optional)"
                }

                let defaultShellAttrs =
                    [ "class", "input-row-field input-row-default"; "data-input-default-shell", "true" ]
                    @ UiHtml.whenAttr isRequired "hidden" "hidden"

                let defaultShell = UiHtml.attrs defaultShellAttrs (div ())

                defaultShell { UiHtml.textInput "InputDefaultValue" defaultValue false "Default value (optional)" }

                let configShell =
                    UiHtml.attrs [ "class", "input-row-field input-row-config"; "data-input-type-config-shell", "true" ] (div ())

                configShell {
                    UiHtml.textInputWithAttrs
                        "InputTypeConfig"
                        typeConfig
                        false
                        "Max length or comma options"
                        [ "data-input-type-config", "true" ]

                    let configHelp =
                        UiHtml.attrs [ "class", "input-config-help"; "data-input-type-config-help", "true" ] (small ())

                    configHelp { "Optional type configuration" }
                }
            }

        let container = UiHtml.attrs [ "class", "input-rows"; "data-input-rows", "true" ] (div ())

        container {
            for input in inputs do
                row (Some input) false

            row None true

            let addButton =
                UiHtml.attrs [ "type", "button"; "class", "button button-secondary input-add-button"; "data-add-input-row", "true" ] (button ())

            addButton { "Add input" }
        }

    let private methodSelect (selectedMethod: string) =
        let selectTag = UiHtml.attrs [ "name", "HttpMethod"; "required", "required" ] (select ())

        selectTag {
            for method in [ "GET"; "POST"; "PUT"; "PATCH"; "DELETE" ] do
                UiHtml.optionTag method method (String.Equals(selectedMethod, method, StringComparison.OrdinalIgnoreCase))
        }

    let private sqlQueryModeValue mode =
        match mode with
        | SqlQueryMode.Gui -> "gui"
        | SqlQueryMode.Raw -> "raw"

    let private sqlFilterOperatorValue op =
        match op with
        | SqlFilterOperator.Equals -> "="
        | SqlFilterOperator.NotEquals -> "!="
        | SqlFilterOperator.GreaterThan -> ">"
        | SqlFilterOperator.GreaterThanOrEqual -> ">="
        | SqlFilterOperator.LessThan -> "<"
        | SqlFilterOperator.LessThanOrEqual -> "<="
        | SqlFilterOperator.Like -> "LIKE"
        | SqlFilterOperator.ILike -> "ILIKE"
        | SqlFilterOperator.In -> "IN"
        | SqlFilterOperator.NotIn -> "NOT IN"
        | SqlFilterOperator.IsNull -> "IS NULL"
        | SqlFilterOperator.IsNotNull -> "IS NOT NULL"

    let private sqlSortDirectionValue direction =
        match direction with
        | SqlSortDirection.Asc -> "ASC"
        | SqlSortDirection.Desc -> "DESC"

    let private sqlFilterOperatorSelect (selectedValue: string) =
        let selectTag =
            UiHtml.attrs [ "name", "SqlFilterOperator"; "aria-label", "Filter operator"; "data-sql-filter-operator", "true" ] (select ())

        selectTag {
            for op in [ "="; "!="; ">"; ">="; "<"; "<="; "IN"; "NOT IN"; "LIKE"; "ILIKE"; "IS NULL"; "IS NOT NULL" ] do
                UiHtml.optionTag op op (selectedValue = op)
        }

    let private sqlSortDirectionSelect (selectedValue: string) =
        let selectTag = UiHtml.attrs [ "name", "SqlOrderByDirection"; "aria-label", "Sort direction" ] (select ())

        selectTag {
            UiHtml.optionTag "ASC" "ASC" (selectedValue = "ASC")
            UiHtml.optionTag "DESC" "DESC" (selectedValue = "DESC")
        }

    let private sqlModeToggle (selectedMode: string) =
        div (class' = "sql-mode-toggle") {
            for value, labelText in [ "gui", "GUI builder"; "raw", "Raw SQL" ] do
                label (class' = if selectedMode = value then "sql-mode-option is-active" else "sql-mode-option") {
                    let radio =
                        UiHtml.attrs
                            ([ "type", "radio"
                               "name", "SqlMode"
                               "value", value
                               "data-sql-mode-control", "true" ]
                             @ UiHtml.checkedAttr (selectedMode = value))
                            (input ())

                    radio
                    span () { labelText }
                }
        }

    let private sqlRowRemoveButton () =
        let removeButton =
            UiHtml.attrs [ "type", "button"; "class", "button button-ghost sql-row-remove-button"; "data-remove-row", "true" ] (button ())

        removeButton { "Remove" }

    let private sqlColumnRows (columns: string list) =
        let row (value: string) (template: bool) =
            let rowClass = if template then "sql-builder-row sql-column-row sql-row-template" else "sql-builder-row sql-column-row"

            div (class' = rowClass) {
                UiHtml.textInput "SqlColumn" value false "Column name, e.g. customers.email"
                sqlRowRemoveButton ()
            }

        let container = UiHtml.attrs [ "class", "sql-builder-rows"; "data-sql-rows", "true" ] (div ())

        container {
            for column in columns do
                row column false

            row String.Empty true

            let addButton =
                UiHtml.attrs [ "type", "button"; "class", "button button-secondary"; "data-add-sql-row", "true" ] (button ())

            addButton { "Add column" }
        }

    let private sqlFilterRows (filters: SqlFilter list) =
        let row (filter: SqlFilter option) (template: bool) =
            let column = filter |> Option.map (fun value -> value.Column) |> Option.defaultValue String.Empty
            let selectedOperator = filter |> Option.map (fun value -> sqlFilterOperatorValue value.Operator) |> Option.defaultValue "="
            let value = filter |> Option.bind (fun value -> value.Value) |> Option.defaultValue String.Empty
            let rowClass = if template then "sql-builder-row sql-filter-row sql-row-template" else "sql-builder-row sql-filter-row"

            div (class' = rowClass) {
                UiHtml.textInput "SqlFilterColumn" column false "Column"
                sqlFilterOperatorSelect selectedOperator
                UiHtml.textInputWithAttrs
                    "SqlFilterValue"
                    value
                    false
                    "Value, @InputName, or comma list"
                    [ "data-template-input", "true"; "data-sql-filter-value", "true" ]
                sqlRowRemoveButton ()
            }

        let container = UiHtml.attrs [ "class", "sql-builder-rows"; "data-sql-rows", "true" ] (div ())

        container {
            for filter in filters do
                row (Some filter) false

            row None true

            let addButton =
                UiHtml.attrs [ "type", "button"; "class", "button button-secondary"; "data-add-sql-row", "true" ] (button ())

            addButton { "Add filter" }
        }

    let private sqlOrderRows (orderBy: SqlOrderBy list) =
        let row (order: SqlOrderBy option) (template: bool) =
            let column = order |> Option.map (fun value -> value.Column) |> Option.defaultValue String.Empty
            let direction = order |> Option.map (fun value -> sqlSortDirectionValue value.Direction) |> Option.defaultValue "ASC"
            let rowClass = if template then "sql-builder-row sql-order-row sql-row-template" else "sql-builder-row sql-order-row"

            div (class' = rowClass) {
                UiHtml.textInput "SqlOrderByColumn" column false "Column"
                sqlSortDirectionSelect direction
                sqlRowRemoveButton ()
            }

        let container = UiHtml.attrs [ "class", "sql-builder-rows"; "data-sql-rows", "true" ] (div ())

        container {
            for order in orderBy do
                row (Some order) false

            row None true

            let addButton =
                UiHtml.attrs [ "type", "button"; "class", "button button-secondary"; "data-add-sql-row", "true" ] (button ())

            addButton { "Add sort" }
        }

    let private sqlQueryConfigFields (config: SqlQueryConfig option) =
        let selectedMode = config |> Option.map (fun value -> sqlQueryModeValue value.Mode) |> Option.defaultValue "gui"
        let table = config |> Option.bind (fun value -> value.Table) |> Option.defaultValue String.Empty
        let columns = config |> Option.map (fun value -> value.Columns) |> Option.defaultValue []
        let filters = config |> Option.map (fun value -> value.Filters) |> Option.defaultValue []
        let limit =
            match config with
            | None -> "100"
            | Some value -> value.Limit |> Option.map string |> Option.defaultValue String.Empty
        let orderBy = config |> Option.map (fun value -> value.OrderBy) |> Option.defaultValue []
        let rawSql = config |> Option.bind (fun value -> value.RawSql) |> Option.defaultValue "select 1"
        let rawSqlParams =
            config
            |> Option.map (fun value -> value.RawSqlParams |> List.map keyValuePairDto)
            |> Option.defaultValue []

        let builder = UiHtml.attrs [ "class", "sql-builder"; "data-sql-builder", "true" ] (div ())

        builder {
            sqlModeToggle selectedMode

            let guiSectionAttrs =
                [ "class", "sql-builder-panel"; "data-sql-mode-section", "gui" ]
                @ UiHtml.whenAttr (selectedMode <> "gui") "hidden" "hidden"

            let guiSection = UiHtml.attrs guiSectionAttrs (section ())

            guiSection {
                div (class' = "form-grid") {
                    field "Table" (UiHtml.textInput "SqlTable" table false "public.customers") (Some "Required for GUI mode. Use schema.table when needed.")
                    field
                        "Limit"
                        (UiHtml.attrs [ "type", "number"; "name", "SqlLimit"; "value", limit; "placeholder", "100" ] (input ()))
                        (Some "Optional. Leave blank for no limit.")
                }

                div (class' = "form-section") {
                    h3 () { "Columns" }
                    p (class' = "muted") { "Leave empty to select all columns. Add one column per row for an explicit projection." }
                    sqlColumnRows columns
                }

                div (class' = "form-section") {
                    h3 () { "Filters" }
                    p (class' = "muted") { "Filter values support @InputName and {{ expression }} templates. IN and NOT IN accept comma-separated values." }
                    sqlFilterRows filters
                }

                div (class' = "form-section") {
                    h3 () { "Order by" }
                    p (class' = "muted") { "Add optional sort columns." }
                    sqlOrderRows orderBy
                }
            }

            let rawSectionAttrs =
                [ "class", "sql-builder-panel"; "data-sql-mode-section", "raw" ]
                @ UiHtml.whenAttr (selectedMode <> "raw") "hidden" "hidden"

            let rawSection = UiHtml.attrs rawSectionAttrs (section ())

            rawSection {
                label (class' = "field") {
                    span () { "SQL" }
                    UiHtml.textareaInputWithAttrs "RawSql" rawSql 10 [ "data-template-input", "true" ]
                    small () { "Use @InputName, @\"Input Name\", and {{ expression }} templates. Define named SQL parameters below when your query uses @param placeholders." }
                }

                div (class' = "form-section") {
                    h3 () { "SQL parameters" }
                    p (class' = "muted") { "Optional named parameter values for raw SQL. Values support templates." }
                    kvRows "RawSqlParam" rawSqlParams
                }
            }
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

    let private readOnlyKvSummary (titleText: string) (pairs: KeyValuePair list) =
        let attrs =
            [ "class", "resource-default-group" ]
            @ if List.isEmpty pairs then [ "hidden", "hidden" ] else []

        let group = UiHtml.attrs attrs (div ())

        group {
            strong () { titleText }
            div (class' = "resource-default-list") {
                for pair in pairs do
                    div (class' = "resource-default-row") {
                        code () { pair.Key }
                        span () { pair.Value }
                    }
            }
        }

    let private httpResourcePreview (resource: ResourceData) (hidden: bool) =
        let attrs =
            [ "class", "resource-preview"; "data-resource-preview", resourceId resource ]
            @ UiHtml.whenAttr hidden "hidden" "hidden"

        let preview = UiHtml.attrs attrs (section ())

        preview {
            h4 () { "Resource defaults" }
            div (class' = "resource-default-group") {
                strong () { "Base URL" }
                code () { resource.BaseUrl |> Option.map string |> Option.defaultValue "No base URL configured" }
            }
            readOnlyKvSummary "Default query parameters" resource.UrlParameters
            readOnlyKvSummary "Default headers" resource.Headers
            readOnlyKvSummary "Default JSON body" resource.Body
            p (class' = "muted") { "These resource-level defaults are inherited when this app runs. Add app-level values below to extend them." }
        }

    let private sqlResourcePreview (resource: ResourceData) =
        section (class' = "resource-preview") {
            h4 () { "Resource connection" }
            div (class' = "resource-default-group") {
                strong () { "Database" }
                code () {
                    let host = resource.DatabaseHost |> Option.map string |> Option.defaultValue "host not set"
                    let port = resource.DatabasePort |> Option.map string |> Option.defaultValue "5432"
                    let database = resource.DatabaseName |> Option.map string |> Option.defaultValue "database not set"
                    $"{host}:{port}/{database}"
                }
            }
            readOnlyKvSummary "Connection options" resource.ConnectionOptions
        }

    let private templateHelpText =
        "Template values can reference app inputs with @InputName or @\"Input Name\". Type {{ to open the expression editor for {{ ... }} expressions."

    let private appConfigurationFields (resourceKind: ResourceKind option) (resource: ResourceData option) (inputs: Input list) (app: AppData option) =
        let httpMethod = app |> Option.map (fun value -> value.HttpMethod.ToString()) |> Option.defaultValue "GET"
        let urlPath = app |> Option.bind (fun value -> value.UrlPath) |> Option.defaultValue String.Empty
        let urlParameters = app |> Option.map (fun value -> value.UrlParameters |> List.map keyValuePairDto) |> Option.defaultValue []
        let headers = app |> Option.map (fun value -> value.Headers |> List.map keyValuePairDto) |> Option.defaultValue []
        let body = app |> Option.map (fun value -> value.Body |> List.map keyValuePairDto) |> Option.defaultValue []
        let useDynamicJsonBody = app |> Option.map (fun value -> value.UseDynamicJsonBody) |> Option.defaultValue false
        let sqlConfig = app |> Option.bind (fun value -> value.SqlConfig)

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

                match resource with
                | Some selectedResource when selectedResource.ResourceKind = ResourceKind.Http -> httpResourcePreview selectedResource false
                | _ -> ()

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

                match resource with
                | Some selectedResource when selectedResource.ResourceKind = ResourceKind.Sql -> sqlResourcePreview selectedResource
                | _ -> ()

                sqlQueryConfigFields sqlConfig
            }
        }

    type private BreadcrumbPill = { Label: string; Href: string option }

    let private breadcrumbPills (items: BreadcrumbPill list) : HtmlElement =
        let navTag = UiHtml.attrs [ "class", "breadcrumb-pills"; "aria-label", "Breadcrumb" ] (nav ())

        navTag {
            ol () {
                for item in items do
                    li () {
                        match item.Href with
                        | Some href ->
                            let link = UiHtml.attrs [ "href", href; "class", "breadcrumb-pill" ] (a ())
                            link { item.Label }
                        | None -> span (class' = "breadcrumb-pill is-current") { item.Label }
                    }
            }
        }

    let private spacesBreadcrumbItem isCurrent = {
        Label = "Spaces"
        Href = if isCurrent then None else Some "/spaces"
    }

    let private spaceBreadcrumbItem (space: SpaceData) isCurrent = {
        Label = space.Name
        Href = if isCurrent then None else Some $"/spaces/{spaceId space}"
    }

    let private folderBreadcrumbItems (space: SpaceData) (folderPath: FolderData list) (currentFolderId: FolderId option) =
        let sid = spaceId space

        folderPath
        |> List.map (fun folder -> {
            Label = folder.Name.Value
            Href = if currentFolderId = Some folder.Id then None else Some $"/spaces/{sid}/{folderId folder}"
        })

    let private spacesListBreadcrumb () = breadcrumbPills [ spacesBreadcrumbItem true ]

    let private spaceHomeBreadcrumb (space: SpaceData) =
        breadcrumbPills [ spacesBreadcrumbItem false; spaceBreadcrumbItem space true ]

    let private spaceSectionBreadcrumb (space: SpaceData) (sectionLabel: string) =
        breadcrumbPills [
            spacesBreadcrumbItem false
            spaceBreadcrumbItem space false
            { Label = sectionLabel; Href = None }
        ]

    let private folderBreadcrumb (space: SpaceData) (folderPath: FolderData list) =
        let currentFolderId = folderPath |> List.tryLast |> Option.map (fun folder -> folder.Id)

        breadcrumbPills
            ([ spacesBreadcrumbItem false; spaceBreadcrumbItem space false ]
             @ folderBreadcrumbItems space folderPath currentFolderId)

    let private nodeBreadcrumb (space: SpaceData) (folderPath: FolderData list) (labelText: string) =
        breadcrumbPills
            ([ spacesBreadcrumbItem false; spaceBreadcrumbItem space false ]
             @ folderBreadcrumbItems space folderPath None
             @ [ { Label = labelText; Href = None } ])

    let private runBreadcrumb
        (space: SpaceData)
        (folderPath: FolderData list)
        (nodeHref: string)
        (nodeLabel: string)
        (runLabel: string)
        =
        breadcrumbPills
            ([ spacesBreadcrumbItem false; spaceBreadcrumbItem space false ]
             @ folderBreadcrumbItems space folderPath None
             @ [ { Label = nodeLabel; Href = Some nodeHref }; { Label = runLabel; Href = None } ])

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
            div (class' = "resource-preview-stack") {
                for resource in resources do
                    match resource.ResourceKind with
                    | ResourceKind.Http -> httpResourcePreview resource true
                    | ResourceKind.Sql -> ()
            }
            appConfigurationFields None None [] None
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
            spacesListBreadcrumb ()
            emptyState "No spaces yet" "Create a space to start building internal tools."

            if isOrgAdmin then
                div (class' = "actions") { modalOpenButton modalId "New Space" "plus" "button" }
                modalDialog modalId "Create space" (Some "Spaces group resources, folders, apps, dashboards, and permissions.") (createSpaceForm token users)
        }

    let spacesList (token: string) (spaces: SpaceData list) (users: UserData list) (isOrgAdmin: bool) =
        let modalId = "create-space-modal"

        section (class' = "stack") {
            spacesListBreadcrumb ()
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
            spaceHomeBreadcrumb space
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
        (folderPath: FolderData list)
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

        let effectiveFolderPath =
            if folderPath |> List.exists (fun item -> item.Id = folder.Id) then
                folderPath
            else
                folderPath @ [ folder ]

        section (class' = "stack") {
            folderBreadcrumb space effectiveFolderPath
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

    let private resourceKindFormValue (resourceKind: ResourceKind) =
        match resourceKind with
        | ResourceKind.Http -> "http"
        | ResourceKind.Sql -> "sql"

    let private resourceKindLabel (resourceKind: ResourceKind) =
        match resourceKind with
        | ResourceKind.Http -> "HTTP"
        | ResourceKind.Sql -> "PostgreSQL"

    let private resourceEndpointSummary (resource: ResourceData) =
        match resource.ResourceKind with
        | ResourceKind.Http -> resource.BaseUrl |> Option.map string |> Option.defaultValue "No base URL configured"
        | ResourceKind.Sql ->
            let host = resource.DatabaseHost |> Option.map string |> Option.defaultValue "host not set"
            let port = resource.DatabasePort |> Option.map string |> Option.defaultValue "5432"
            let database = resource.DatabaseName |> Option.map string |> Option.defaultValue "database not set"
            $"{host}:{port}/{database}"

    let private resourceKindSelect (selectedKind: string) =
        let selectTag =
            UiHtml.attrs [ "name", "Kind"; "data-resource-form-kind-select", "true"; "aria-label", "Resource kind" ] (select ())

        selectTag {
            UiHtml.optionTag "http" "HTTP" (selectedKind = "http")
            UiHtml.optionTag "sql" "PostgreSQL" (selectedKind = "sql")
        }

    let private resourceFormKindSection (kind: string) (selectedKind: string) =
        let hiddenAttrs = if kind = selectedKind then [] else [ "hidden", "hidden" ]

        UiHtml.attrs
            ([ "class", "resource-form-kind-section"; "data-resource-form-kind-section", kind ] @ hiddenAttrs)
            (section ())

    let private numberInput (name: string) (value: string) (placeholder: string) =
        UiHtml.attrs [ "type", "number"; "name", name; "value", value; "placeholder", placeholder ] (input ())

    let private passwordInput (name: string) (placeholder: string) =
        UiHtml.attrs [ "type", "password"; "name", name; "value", String.Empty; "placeholder", placeholder; "autocomplete", "new-password" ] (input ())

    let private databaseEngineSelect (selectedValue: string) =
        let selectTag = UiHtml.attrs [ "name", "DatabaseEngine"; "aria-label", "Database engine" ] (select ())

        selectTag { UiHtml.optionTag "POSTGRES" "Postgres" (selectedValue = "POSTGRES") }

    let private databaseAuthSchemeSelect (selectedValue: string) =
        let selectTag = UiHtml.attrs [ "name", "DatabaseAuthScheme"; "aria-label", "Database auth scheme" ] (select ())

        selectTag { UiHtml.optionTag "USERNAME_PASSWORD" "Username + Password" (selectedValue = "USERNAME_PASSWORD") }

    let private httpResourceFields (resource: ResourceData option) =
        let baseUrl = resource |> Option.bind (fun value -> value.BaseUrl) |> Option.map string |> Option.defaultValue String.Empty
        let urlParameters = resource |> Option.map (fun value -> value.UrlParameters |> List.map keyValuePairDto) |> Option.defaultValue []
        let headers = resource |> Option.map (fun value -> value.Headers |> List.map headerKeyValuePairDto) |> Option.defaultValue []
        let body = resource |> Option.map (fun value -> value.Body |> List.map keyValuePairDto) |> Option.defaultValue []

        div (class' = "resource-form-fields") {
            field "Base URL" (UiHtml.textInput "BaseUrl" baseUrl false "https://example.internal") None

            div (class' = "form-section") {
                h3 () { "Default query parameters" }
                kvRows "UrlParameter" urlParameters
            }

            div (class' = "form-section") {
                h3 () { "Default headers" }
                p (class' = "muted") { "Authorization header values are redacted. Leave the redacted value unchanged to keep the existing secret." }
                kvRows "Header" headers
            }

            div (class' = "form-section") {
                h3 () { "Default JSON body" }
                kvRows "Body" body
            }
        }

    let private sqlResourceFields (resource: ResourceData option) =
        let databaseName = resource |> Option.bind (fun value -> value.DatabaseName) |> Option.map string |> Option.defaultValue String.Empty
        let databaseHost = resource |> Option.bind (fun value -> value.DatabaseHost) |> Option.map string |> Option.defaultValue String.Empty
        let databasePort = resource |> Option.bind (fun value -> value.DatabasePort) |> Option.map string |> Option.defaultValue "5432"
        let databaseEngine = resource |> Option.bind (fun value -> value.DatabaseEngine) |> Option.map string |> Option.defaultValue "POSTGRES"
        let databaseAuthScheme = resource |> Option.bind (fun value -> value.DatabaseAuthScheme) |> Option.map string |> Option.defaultValue "USERNAME_PASSWORD"
        let databaseUsername = resource |> Option.bind (fun value -> value.DatabaseUsername) |> Option.map string |> Option.defaultValue String.Empty
        let useSsl = resource |> Option.map (fun value -> value.UseSsl) |> Option.defaultValue true
        let enableSshTunnel = resource |> Option.map (fun value -> value.EnableSshTunnel) |> Option.defaultValue false
        let connectionOptions = resource |> Option.map (fun value -> value.ConnectionOptions |> List.map keyValuePairDto) |> Option.defaultValue []
        let passwordHelp = if resource.IsSome then "Leave blank to keep the existing password." else "Required for new SQL resources."

        div (class' = "resource-form-fields") {
            div (class' = "form-grid") {
                field "Database name" (UiHtml.textInput "DatabaseName" databaseName false "analytics") None
                field "Host" (UiHtml.textInput "DatabaseHost" databaseHost false "db.internal") None
                field "Port" (numberInput "DatabasePort" databasePort "5432") None
                field "Engine" (databaseEngineSelect databaseEngine) None
                field "Auth scheme" (databaseAuthSchemeSelect databaseAuthScheme) None
                field "Username" (UiHtml.textInput "DatabaseUsername" databaseUsername false "db_user") None
                field "Password" (passwordInput "DatabasePassword" "••••••••") (Some passwordHelp)
            }

            div (class' = "resource-toggle-grid") {
                label (class' = "resource-toggle-card") {
                    div () {
                        strong () { "Use SSL/TLS" }
                        small () { "Encrypt connections to the database." }
                    }
                    UiHtml.checkbox "UseSsl" "true" useSsl
                }

                label (class' = "resource-toggle-card") {
                    div () {
                        strong () { "Enable SSH tunnel" }
                        small () { "Connect through an SSH bastion when configured." }
                    }
                    UiHtml.checkbox "EnableSshTunnel" "true" enableSshTunnel
                }
            }

            div (class' = "form-section") {
                h3 () { "Connection options" }
                kvRows "ConnectionOption" connectionOptions
            }
        }

    let private resourceForm (token: string) (action: string) (submitLabel: string) (resource: ResourceData option) =
        let selectedKind = resource |> Option.map (fun value -> resourceKindFormValue value.ResourceKind) |> Option.defaultValue "http"
        let isEdit = resource.IsSome

        let formAttrs =
            [ "data-resource-form", "true" ]
            @ if isEdit then [ "data-track-dirty", "true" ] else []

        let formTag = UiHtml.enhancedPostForm action formAttrs

        formTag {
            UiHtml.antiforgeryInput token
            let name = resource |> Option.map (fun value -> value.Name.Value) |> Option.defaultValue String.Empty
            let description = resource |> Option.map (fun value -> value.Description.Value) |> Option.defaultValue String.Empty

            div (class' = "form-grid") {
                field "Name" (UiHtml.textInput "Name" name true "Core API") None
                if isEdit then
                    field "Kind" (span (class' = "badge") { resource |> Option.map (fun value -> resourceKindLabel value.ResourceKind) |> Option.defaultValue "HTTP" }) None
                    UiHtml.hidden "Kind" selectedKind
                else
                    field "Kind" (resourceKindSelect selectedKind) None
            }

            label (class' = "field") {
                span () { "Description" }
                UiHtml.textareaInput "Description" description 3
            }

            if isEdit then
                match resource with
                | Some value when value.ResourceKind = ResourceKind.Sql ->
                    let sqlSection = resourceFormKindSection "sql" selectedKind
                    sqlSection { sqlResourceFields resource }
                | _ ->
                    let httpSection = resourceFormKindSection "http" selectedKind
                    httpSection { httpResourceFields resource }
            else
                let httpSection = resourceFormKindSection "http" selectedKind
                httpSection { httpResourceFields None }

                let sqlSection = resourceFormKindSection "sql" selectedKind
                sqlSection { sqlResourceFields None }

            if isEdit then
                div (class' = "form-actions dirty-actions") {
                    let dirtyStatus = UiHtml.attrs [ "class", "dirty-status"; "data-dirty-status", "true" ] (span ())
                    dirtyStatus { "No unsaved changes" }

                    let discardButton =
                        UiHtml.attrs [ "type", "button"; "class", "button button-ghost"; "data-dirty-reset", "true"; "disabled", "disabled" ] (button ())

                    discardButton { "Discard changes" }

                    let saveButton =
                        UiHtml.attrs [ "type", "submit"; "class", "button"; "data-dirty-submit", "true"; "disabled", "disabled" ] (button ())

                    saveButton { submitLabel }
                }
            else
                UiHtml.submitButton submitLabel
        }

    let resourcesPage (token: string) (space: SpaceData) (resources: ResourceData list) (apps: AppData list) (permissions: SpacePermissionsDto) =
        let sid = spaceId space
        let createResourceModalId = $"create-resource-{sid}"

        section (class' = "stack") {
            spaceSectionBreadcrumb space "Resources"
            spaceTabs space "resources"
            section (class' = "card") {
                div (class' = "toolbar") {
                    div () { cardHeader "Resources" (Some "HTTP and PostgreSQL connections") }
                    if permissions.CreateResource then
                        modalOpenButton createResourceModalId "New Resource" "plus" "button"
                }
            }

            if List.isEmpty resources then
                emptyState "No resources yet" "Create an HTTP or PostgreSQL resource before adding apps."
            else
                section (class' = "resource-grid") {
                    for resource in resources do
                        let rid = resourceId resource
                        let usedBy = apps |> List.filter (fun app -> app.ResourceId = resource.Id)
                        let editModalId = $"edit-resource-{rid}"
                        let deleteConfirmText =
                            if List.isEmpty usedBy then
                                $"Delete resource {resource.Name.Value}?"
                            else
                                let appCount = List.length usedBy
                                let appLabel = if appCount = 1 then "app uses" else "apps use"
                                $"Delete resource {resource.Name.Value}? {appCount} {appLabel} this resource and may stop running."

                        article (class' = "resource-card") {
                            header (class' = "resource-card-header") {
                                UiHtml.attrs [ "src", (if resource.ResourceKind = ResourceKind.Http then "/assets/http.svg" else "/assets/postgres.png"); "alt", ""; "class", "resource-icon" ] (img ())
                                div () {
                                    h3 () { resource.Name.Value }
                                    p () { resource.Description.Value }
                                }
                            }

                            div (class' = "resource-meta") {
                                span (class' = "badge") { resourceKindLabel resource.ResourceKind }
                                span (class' = "badge badge-muted") { $"Used by {List.length usedBy} apps" }
                            }

                            div (class' = "resource-detail-list") {
                                div () {
                                    strong () { if resource.ResourceKind = ResourceKind.Http then "Base URL" else "Database" }
                                    code () { resourceEndpointSummary resource }
                                }

                                match resource.ResourceKind with
                                | ResourceKind.Http ->
                                    div () {
                                        strong () { "Defaults" }
                                        span () { $"{List.length resource.UrlParameters} query · {List.length resource.Headers} headers · {List.length resource.Body} body" }
                                    }
                                | ResourceKind.Sql ->
                                    div () {
                                        let sslState = if resource.UseSsl then "on" else "off"
                                        let sshState = if resource.EnableSshTunnel then "on" else "off"

                                        strong () { "Security" }
                                        span () { $"SSL: {sslState} · SSH tunnel: {sshState}" }
                                    }
                            }

                            div (class' = "object-actions") {
                                if permissions.EditResource then
                                    iconModalOpenButton editModalId "Edit resource" "edit"
                                if permissions.DeleteResource then
                                    iconDeleteForm token $"/_ui/spaces/{sid}/resources/{rid}/delete" "Delete resource" deleteConfirmText
                            }
                        }

                        if permissions.EditResource then
                            modalDialog
                                editModalId
                                $"Edit {resource.Name.Value}"
                                (Some "Changes are saved to the resource defaults used by apps at run time.")
                                (resourceForm token $"/_ui/spaces/{sid}/resources/{rid}/update" "Save resource" (Some resource))
                }

            if permissions.CreateResource then
                modalDialog
                    createResourceModalId
                    "Create resource"
                    (Some "Create an HTTP API or PostgreSQL connection for apps in this space.")
                    (resourceForm token $"/_ui/spaces/{sid}/resources/create" "Create resource" None)
        }

    let appEditor (token: string) (space: SpaceData) (folderPath: FolderData list) (app: AppData) (resource: ResourceData) =
        let sid = spaceId space
        let aid = appId app
        section (class' = "stack") {
            nodeBreadcrumb space folderPath app.Name
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
                cardHeader "Build configuration" (Some "Edit inputs and request templates. Use @InputName, @\"Input Name\", and type {{ to insert {{ expression }} syntax in values.")
                let formTag11 =
                    UiHtml.enhancedPostForm
                        $"/_ui/spaces/{sid}/apps/{aid}/config"
                        [ "data-app-config-form", "true"
                          "data-track-dirty", "true" ]

                formTag11 {
                    UiHtml.antiforgeryInput token
                    appConfigurationFields (Some resource.ResourceKind) (Some resource) app.Inputs (Some app)
                    div (class' = "form-actions dirty-actions") {
                        let dirtyStatus = UiHtml.attrs [ "class", "dirty-status"; "data-dirty-status", "true" ] (span ())
                        dirtyStatus { "No unsaved changes" }

                        let discardButton =
                            UiHtml.attrs
                                [ "type", "button"
                                  "class", "button button-ghost"
                                  "data-dirty-reset", "true"
                                  "disabled", "disabled" ]
                                (button ())

                        discardButton { "Discard changes" }

                        let saveButton =
                            UiHtml.attrs
                                [ "type", "submit"
                                  "class", "button"
                                  "data-dirty-submit", "true"
                                  "disabled", "disabled" ]
                                (button ())

                        saveButton { "Save configuration" }
                    }
                }
            }
        }

    let private submittedRunInputValues (run: RunData) =
        run.InputValues |> List.map (fun inputValue -> inputValue.Title, inputValue.Value) |> Map.ofList

    let private runDefaultInputValue (appInput: Input) =
        match appInput.DefaultValue with
        | Some defaultValue ->
            match defaultValue.Value with
            | DateDefault date -> date.ToString("yyyy-MM-dd")
            | _ -> defaultValue.ToRawString()
        | None -> String.Empty

    let private runInputValue (submittedValues: Map<string, string>) (appInput: Input) =
        submittedValues |> Map.tryFind appInput.Title |> Option.defaultValue (runDefaultInputValue appInput)

    let private runTextControl
        (name: string)
        (value: string)
        (required: bool)
        (htmlType: string)
        (placeholder: string)
        (extraAttrs: (string * string) list)
        : HtmlElement =
        UiHtml.attrs
            ([ "type", htmlType
               "name", name
               "value", value
               "placeholder", placeholder
               "aria-label", name ]
             @ UiHtml.requiredAttr required
             @ extraAttrs)
            (input ())

    let private runSelectControl (name: string) (required: bool) (selectedValue: string) (choices: (string * string) list) : HtmlElement =
        let selectTag =
            UiHtml.attrs ([ "name", name; "aria-label", name ] @ UiHtml.requiredAttr required) (select ())

        selectTag {
            let placeholder =
                UiHtml.attrs ([ "value", "" ] @ UiHtml.selectedAttr (String.IsNullOrWhiteSpace selectedValue)) (option ())

            placeholder { "Choose an option" }

            for value, labelText in choices do
                UiHtml.optionTag value labelText (selectedValue = value)
        }

    let private runBooleanControl (name: string) (selectedValue: string) : HtmlElement =
        let checkedValue =
            if String.Equals(selectedValue, "true", StringComparison.OrdinalIgnoreCase) then
                "true"
            else
                "false"

        div (class' = "run-segmented-options") {
            for value, labelText in [ "true", "Yes"; "false", "No" ] do
                label (class' = "run-choice-pill") {
                    UiHtml.attrs
                        ([ "type", "radio"
                           "name", name
                           "value", value
                           "aria-label", $"{name} {labelText}" ]
                         @ UiHtml.checkedAttr (checkedValue = value))
                        (input ())
                    span () { labelText }
                }
        }

    let private runRadioControl (name: string) (required: bool) (selectedValue: string) (options: RadioOption list) : HtmlElement =
        div (class' = "run-choice-list") {
            for radioOption in options do
                label (class' = "run-choice-pill") {
                    UiHtml.attrs
                        ([ "type", "radio"
                           "name", name
                           "value", radioOption.Value
                           "aria-label", $"{name} {radioOption.Value}" ]
                         @ UiHtml.requiredAttr required
                         @ UiHtml.checkedAttr (selectedValue = radioOption.Value))
                        (input ())
                    span () { radioOption.Label |> Option.defaultValue radioOption.Value }
                }
        }

    let private runInputControl (submittedValues: Map<string, string>) (appInput: Input) : HtmlElement =
        let value = runInputValue submittedValues appInput
        let required = appInput.Required
        let placeholder = $"Enter {appInput.Title}"

        match appInput.Type.Value with
        | InputTypeValue.Boolean -> runBooleanControl appInput.Title value
        | InputTypeValue.Radio options ->
            options
            |> List.map (fun option -> option.Value, (option.Label |> Option.defaultValue option.Value))
            |> runSelectControl appInput.Title required value
        | InputTypeValue.MultiEmail allowedEmails ->
            allowedEmails
            |> List.map (fun email -> string email, string email)
            |> runSelectControl appInput.Title required value
        | InputTypeValue.MultiDate allowedDates ->
            allowedDates
            |> List.map (fun date -> date.ToString("yyyy-MM-dd"), date.ToString("yyyy-MM-dd"))
            |> runSelectControl appInput.Title required value
        | InputTypeValue.MultiText(_, allowedValues) ->
            allowedValues |> List.map (fun allowedValue -> allowedValue, allowedValue) |> runSelectControl appInput.Title required value
        | InputTypeValue.MultiInteger allowedIntegers ->
            allowedIntegers
            |> List.map (fun allowedInteger -> string allowedInteger, string allowedInteger)
            |> runSelectControl appInput.Title required value
        | InputTypeValue.Email -> runTextControl appInput.Title value required "email" placeholder []
        | InputTypeValue.Date -> runTextControl appInput.Title value required "date" placeholder []
        | InputTypeValue.Integer -> runTextControl appInput.Title value required "number" placeholder [ "step", "1"; "inputmode", "numeric" ]
        | InputTypeValue.Currency SupportedCurrency.USD ->
            runTextControl
                appInput.Title
                value
                required
                "number"
                $"Enter {appInput.Title} (USD)"
                [ "min", "0"; "step", "0.01"; "inputmode", "decimal" ]
        | InputTypeValue.Text maxLength ->
            runTextControl appInput.Title value required "text" placeholder [ "maxlength", string maxLength ]

    let private runInputCard (submittedValues: Map<string, string>) (appInput: Input) =
        article (class' = "run-input-card") {
            div (class' = "run-input-label-row") {
                div () {
                    span (class' = "run-input-title") {
                        appInput.Title
                        if appInput.Required then
                            span (class' = "required-marker") { "*" }
                    }

                    match appInput.Description with
                    | Some description when not (String.IsNullOrWhiteSpace description) -> p () { description }
                    | _ -> ()
                }

                span (class' = "input-type-pill") { UiFormat.inputType appInput.Type }
            }

            runInputControl submittedValues appInput
        }

    let private runStatusClass =
        function
        | RunStatus.Success -> "run-status-success"
        | RunStatus.Failure -> "run-status-failure"
        | RunStatus.InvalidConfiguration -> "run-status-warning"
        | RunStatus.Running -> "run-status-running"
        | RunStatus.Pending -> "run-status-pending"

    let private runStatusMessage (run: RunData) =
        match run.Status with
        | RunStatus.Success -> "App executed successfully."
        | RunStatus.Failure -> run.ErrorMessage |> Option.defaultValue "The app execution failed."
        | RunStatus.InvalidConfiguration ->
            run.ErrorMessage |> Option.defaultValue "The app has an invalid configuration."
        | RunStatus.Running -> "The app is still running."
        | RunStatus.Pending -> "The run is waiting to start."

    let private runDate value = value |> Option.map UiFormat.dateTime |> Option.defaultValue "—"

    let private runDuration (run: RunData) =
        match run.StartedAt, run.CompletedAt with
        | Some startedAt, Some completedAt ->
            let seconds = (completedAt.ToUniversalTime() - startedAt.ToUniversalTime()).TotalSeconds
            seconds.ToString("0.00") + "s"
        | _ -> "—"

    let private runMetaItem (labelText: string) (value: string) =
        div (class' = "run-meta-item") {
            span () { labelText }
            strong () { value }
        }

    let private requestFullUrl (request: ExecutableHttpRequest) =
        if List.isEmpty request.UrlParameters then
            request.BaseUrl
        else
            let query =
                request.UrlParameters
                |> List.map (fun (key, value) -> $"{Uri.EscapeDataString key}={Uri.EscapeDataString value}")
                |> String.concat "&"

            $"{request.BaseUrl}?{query}"

    let private tryJsonValueLiteral (value: string) =
        let trimmed = value.Trim()

        if String.Equals(trimmed, "undefined", StringComparison.OrdinalIgnoreCase) then
            None
        else
            try
                use document = JsonDocument.Parse(trimmed)
                Some(document.RootElement.GetRawText())
            with _ ->
                Some(JsonSerializer.Serialize(value))

    let private keyValueJson (pairs: (string * string) list) =
        let lines =
            pairs
            |> List.choose (fun (key, value) ->
                tryJsonValueLiteral value
                |> Option.map (fun literal -> $"  {JsonSerializer.Serialize(key)}: {literal}"))

        if List.isEmpty lines then
            "{}"
        else
            "{\n" + String.concat ",\n" lines + "\n}"

    let private jsonElementDisplay (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> String.Empty
        | JsonValueKind.String ->
            let value = element.GetString()
            if isNull value then String.Empty else value
        | JsonValueKind.Number
        | JsonValueKind.True
        | JsonValueKind.False -> element.ToString()
        | _ -> element.GetRawText()

    let private tryJsonTable (value: string) =
        if String.IsNullOrWhiteSpace value then
            None
        else
            try
                use document = JsonDocument.Parse(value)
                let root = document.RootElement

                if root.ValueKind = JsonValueKind.Array then
                    let items = root.EnumerateArray() |> Seq.toList

                    if not (List.isEmpty items) && (items |> List.forall (fun item -> item.ValueKind = JsonValueKind.Object)) then
                        let rows =
                            items
                            |> List.map (fun item ->
                                item.EnumerateObject()
                                |> Seq.map (fun property -> property.Name, jsonElementDisplay property.Value)
                                |> Map.ofSeq)

                        let columns =
                            rows
                            |> List.collect (fun row -> row |> Map.toList |> List.map fst)
                            |> List.distinct

                        Some(columns, rows)
                    else
                        None
                else
                    None
            with _ ->
                None

    let private tryJsonKind (value: string) =
        try
            use document = JsonDocument.Parse(value)
            Some document.RootElement.ValueKind
        with _ ->
            None

    let private runKeyValueRows (title: string) (pairs: (string * string) list) =
        let container =
            UiHtml.attrs ([ "class", "run-detail-group" ] @ UiHtml.whenAttr (List.isEmpty pairs) "hidden" "hidden") (div ())

        container {
            if not (List.isEmpty pairs) then
                h3 () { title }
                div (class' = "run-kv-list") {
                    for key, value in pairs do
                        div (class' = "run-kv-row") {
                            code () { key }
                            span () { value }
                        }
                }
        }

    let private runKeyValuePairRows (title: string) (pairs: KeyValuePair list) =
        pairs |> List.map (fun pair -> pair.Key, pair.Value) |> runKeyValueRows title

    let private runResponseTable (columns: string list) (rows: Map<string, string> list) =
        div (class' = "table-wrap") {
            table (class' = "run-response-table") {
                thead () {
                    tr () {
                        for column in columns do
                            th () { column }
                    }
                }
                tbody () {
                    for row in rows do
                        tr () {
                            for column in columns do
                                td () { row |> Map.tryFind column |> Option.defaultValue String.Empty }
                        }
                }
            }
        }

    let private runResponseContent (response: string option) =
        match response with
        | Some responseText when not (String.IsNullOrWhiteSpace responseText) ->
            match tryJsonTable responseText with
            | Some(columns, rows) ->
                div (class' = "run-response-stack") {
                    div (class' = "run-response-toolbar") {
                        span () { "Content type: JSON (table view)" }
                        span () { $"{rows.Length} row(s)" }
                    }
                    runResponseTable columns rows
                }
            | None ->
                let contentType =
                    match tryJsonKind responseText with
                    | Some _ -> "JSON"
                    | None -> "Text / HTML"

                div (class' = "run-response-stack") {
                    div (class' = "run-response-toolbar") { span () { $"Content type: {contentType}" } }
                    pre (class' = "code-block") { code () { UiFormat.tryFormatJson responseText } }
                }
        | _ ->
            div (class' = "empty-state run-empty-response") {
                h2 () { "No response body" }
                p () { "The run completed without returning response data." }
            }

    let private runStatusPanel (run: RunData) =
        section (id = "run-result", class' = "card run-status-card sensitive") {
            div (class' = "run-status-heading") {
                div () {
                    p (class' = "eyebrow") { "Execution status" }
                    h2 () { UiFormat.runStatus run.Status }
                }

                span (class' = $"run-status-pill {runStatusClass run.Status}") { UiFormat.runStatus run.Status }
            }

            p (class' = "run-status-message") { runStatusMessage run }

            div (class' = "run-meta-grid") {
                runMetaItem "Run ID" (run.Id.Value.ToString())
                runMetaItem "Started" (runDate run.StartedAt)
                runMetaItem "Completed" (runDate run.CompletedAt)
                runMetaItem "Duration" (runDuration run)
            }

            match run.ErrorMessage with
            | Some error when run.Status <> RunStatus.Success -> p (class' = "flash flash-error") { error }
            | _ -> ()
        }

    let private runSqlDetails (app: AppData) (run: RunData) =
        let sqlConfig = app.SqlConfig
        let sqlText = run.ExecutedSql |> Option.orElse (sqlConfig |> Option.bind (fun config -> config.RawSql))

        div (class' = "run-detail-stack") {
            match sqlText with
            | Some sql when not (String.IsNullOrWhiteSpace sql) ->
                div (class' = "run-detail-group") {
                    h3 () { "SQL query" }
                    pre (class' = "code-block") { code () { sql } }
                }
            | _ -> p (class' = "muted") { "SQL query was not captured for this run." }

            match sqlConfig with
            | Some config ->
                runKeyValuePairRows "SQL parameters" config.RawSqlParams

                div (class' = "run-detail-group") {
                    h3 () { "Query configuration" }
                    div (class' = "run-meta-grid") {
                        runMetaItem "Mode" (sqlQueryModeValue config.Mode)
                        runMetaItem "Table" (config.Table |> Option.defaultValue "Not set")
                        runMetaItem "Columns" (if List.isEmpty config.Columns then "All columns" else String.concat ", " config.Columns)
                        runMetaItem "Limit" (config.Limit |> Option.map string |> Option.defaultValue "None")
                    }
                }

                if not (List.isEmpty config.Filters) then
                    div (class' = "run-detail-group") {
                        h3 () { "Filters" }
                        div (class' = "run-kv-list") {
                            for filter in config.Filters do
                                div (class' = "run-kv-row") {
                                    code () { filter.Column }
                                    span () {
                                        let filterValue = filter.Value |> Option.defaultValue String.Empty
                                        $"{sqlFilterOperatorValue filter.Operator} {filterValue}".Trim()
                                    }
                                }
                        }
                    }

                if not (List.isEmpty config.OrderBy) then
                    div (class' = "run-detail-group") {
                        h3 () { "Order by" }
                        div (class' = "run-kv-list") {
                            for orderBy in config.OrderBy do
                                div (class' = "run-kv-row") {
                                    code () { orderBy.Column }
                                    span () { sqlSortDirectionValue orderBy.Direction }
                                }
                        }
                    }
            | None -> ()
        }

    let private runHttpDetails (run: RunData) =
        div (class' = "run-detail-stack") {
            match run.ExecutableRequest with
            | Some request ->
                div (class' = "run-detail-group") {
                    h3 () { "Method & URL" }
                    div (class' = "run-http-line") {
                        span (class' = "badge") { request.HttpMethod }
                        code () { requestFullUrl request }
                    }
                }

                runKeyValueRows "URL parameters" request.UrlParameters
                runKeyValueRows "Headers" request.Headers

                if not (List.isEmpty request.Body) then
                    div (class' = "run-detail-group") {
                        h3 () { if request.UseJsonBody then "JSON body" else "Body" }
                        pre (class' = "code-block") { code () { keyValueJson request.Body } }
                    }
            | None -> p (class' = "muted") { "Request details were not captured for this run." }
        }

    let private runRequestPanel (app: AppData) (run: RunData) =
        section (class' = "card run-request-card sensitive") {
            let detailsTag = UiHtml.attrs [ "class", "run-details"; "open", "open" ] (details ())

            detailsTag {
                summary () { "Request details" }
                div (class' = "run-detail-stack") {
                    if app.SqlConfig.IsSome || run.ExecutedSql.IsSome then
                        runSqlDetails app run
                    else
                        runHttpDetails run

                    if not (List.isEmpty run.InputValues) then
                        runKeyValueRows "Submitted inputs" (run.InputValues |> List.map (fun inputValue -> inputValue.Title, inputValue.Value))
                }
            }
        }

    let private runResponsePanel (run: RunData) =
        section (class' = "card run-response-card sensitive") {
            cardHeader "Response" (Some "Formatted response data from the latest run.")
            runResponseContent run.Response
        }

    let runAppPage (token: string) (space: SpaceData) (folderPath: FolderData list) (app: AppData) (result: RunData option) =
        let sid = spaceId space
        let aid = appId app
        let submittedValues = result |> Option.map submittedRunInputValues |> Option.defaultValue Map.empty
        let hasInputs = not (List.isEmpty app.Inputs)
        let hasRuntimeInputs = hasInputs || app.UseDynamicJsonBody
        let isSqlApp = app.SqlConfig.IsSome

        section (class' = "stack run-page") {
            runBreadcrumb space folderPath $"/spaces/{sid}/{aid}" app.Name "Run"
            spaceTabs space "builder"

            section (class' = "card run-hero") {
                div (class' = "card-header-actions") {
                    cardHeader $"Run {app.Name}" app.Description
                    div (class' = "actions") {
                        a (href = $"/spaces/{sid}/{aid}", class' = "button button-secondary") { "Edit app" }
                        a (href = $"/audit?scope=app&appId={aid}", class' = "button button-ghost") { "Audit" }
                    }
                }

                div (class' = "run-hero-meta") {
                    span (class' = "badge") { if isSqlApp then "SQL app" else $"{app.HttpMethod} app" }
                    span (class' = "badge badge-muted") { $"{app.Inputs.Length} input(s)" }
                    if app.UseDynamicJsonBody then
                        span (class' = "badge badge-muted") { "Dynamic JSON body" }
                }
            }

            section (class' = "card run-inputs-card") {
                cardHeader
                    (if result.IsSome then "Run again" else if hasRuntimeInputs then "App inputs" else "Run app")
                    (Some "Fill in runtime values, then execute the app.")

                let formTag12 = UiHtml.enhancedPostForm $"/_ui/spaces/{sid}/apps/{aid}/run" [ "class", "run-form" ]

                formTag12 {
                    UiHtml.antiforgeryInput token

                    if hasInputs then
                        div (class' = "run-input-grid") {
                            for appInput in app.Inputs do
                                runInputCard submittedValues appInput
                        }

                    if app.UseDynamicJsonBody then
                        section (class' = "run-dynamic-body") {
                            h3 () { "JSON body parameters" }
                            p (class' = "muted") { "Add key-value pairs to merge into the request body for this run." }
                            kvRows "DynamicBody" []
                        }

                    if not hasRuntimeInputs then
                        emptyState "No inputs required" "This app can run without runtime parameters."

                    div (class' = "form-actions run-form-actions") {
                        let submitButton = UiHtml.attrs [ "type", "submit"; "class", "button run-submit-button" ] (button ())

                        submitButton {
                            span (class' = "button-icon") { iconSvg "play" }
                            if result.IsSome then "Run again" else "Run app"
                        }
                    }
                }
            }

            match result with
            | None -> ()
            | Some run ->
                div (class' = "stack run-result-stack") {
                    runStatusPanel run
                    runRequestPanel app run
                    runResponsePanel run
                }
        }

    let dashboardEditor
        (token: string)
        (space: SpaceData)
        (folderPath: FolderData list)
        (dashboard: DashboardData)
        (apps: AppData list)
        =
        let sid = spaceId space
        let did = dashboardId dashboard
        section (class' = "stack") {
            nodeBreadcrumb space folderPath dashboard.Name.Value
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

    let dashboardRuntimePage
        (token: string)
        (space: SpaceData)
        (folderPath: FolderData list)
        (dashboard: DashboardData)
        (result: string option)
        =
        let sid = spaceId space
        let did = dashboardId dashboard
        section (class' = "stack") {
            runBreadcrumb space folderPath $"/spaces/{sid}/{did}" dashboard.Name.Value "Run"
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
            spaceSectionBreadcrumb space "Settings"
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
                table (class' = "users-table") {
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

    let private auditEventTypes =
        [ "UserCreatedEvent"
          "UserUpdatedEvent"
          "UserDeletedEvent"
          "UserInvitedEvent"
          "UserActivatedEvent"
          "AppCreatedEvent"
          "AppUpdatedEvent"
          "AppDeletedEvent"
          "AppRestoredEvent"
          "DashboardCreatedEvent"
          "DashboardUpdatedEvent"
          "DashboardDeletedEvent"
          "DashboardPreparedEvent"
          "DashboardPrepareFailedEvent"
          "DashboardActionExecutedEvent"
          "DashboardActionFailedEvent"
          "ResourceCreatedEvent"
          "ResourceUpdatedEvent"
          "ResourceDeletedEvent"
          "ResourceRestoredEvent"
          "FolderCreatedEvent"
          "FolderUpdatedEvent"
          "FolderDeletedEvent"
          "FolderRestoredEvent"
          "RunCreatedEvent"
          "RunStatusChangedEvent"
          "SpaceCreatedEvent"
          "SpaceUpdatedEvent"
          "SpaceDeletedEvent"
          "SpacePermissionsChangedEvent"
          "SpaceDefaultMemberPermissionsChangedEvent" ]

    let private auditEntityTypes = [ "User"; "App"; "Dashboard"; "Resource"; "Folder"; "Run"; "Space" ]

    let private auditScopeTitle (scope: string option) =
        match scope with
        | Some "app" -> "App Audit Log"
        | Some "user" -> "User Audit Log"
        | Some "dashboard" -> "Dashboard Audit Log"
        | _ -> "Audit log"

    let private auditQueryHref (pairs: (string * string option) list) =
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

        if String.IsNullOrWhiteSpace query then "/audit" else $"/audit?{query}"

    let private auditScopedPairs (filters: AuditFilterValues) =
        [ "scope", filters.Scope
          "appId", filters.AppId
          "userId", filters.UserId
          "dashboardId", filters.DashboardId ]

    let private auditFilterForm (users: UserData list) (filters: AuditFilterValues) =
        let selected value optionValue = optionValue |> Option.exists ((=) value)
        let scopedPairs = auditScopedPairs filters
        let clearHref = auditQueryHref scopedPairs

        form (method = "get", action = "/audit", class' = "audit-filter-form") {
            for key, value in scopedPairs do
                match value with
                | Some text when not (String.IsNullOrWhiteSpace text) -> UiHtml.hidden key text
                | _ -> ()

            div (class' = "form-grid") {
                field
                    "Search"
                    (UiHtml.textInput "q" (filters.Search |> Option.defaultValue String.Empty) false "Event, entity, actor, or raw JSON")
                    (Some "Searches event summary, entity, actor, IDs, and raw event data.")

                if filters.Scope.IsNone then
                    label (class' = "field") {
                        span () { "Actor user" }
                        let selectTag = UiHtml.attrs [ "name", "actorUserId" ] (select ())

                        selectTag {
                            option (value = "") { "Any actor" }
                            for user in users do
                                UiHtml.optionTag (userId user) ($"{displayUserName user} ({user.Email})") (selected (userId user) filters.ActorUserId)
                        }
                    }

                label (class' = "field") {
                    span () { "Event type" }
                    let selectTag = UiHtml.attrs [ "name", "eventType" ] (select ())

                    selectTag {
                        option (value = "") { "Any event" }
                        for eventType in auditEventTypes do
                            UiHtml.optionTag eventType (eventType.Replace("Event", "")) (selected eventType filters.EventType)
                    }
                }

                label (class' = "field") {
                    span () { "Entity type" }
                    let selectTag = UiHtml.attrs [ "name", "entityType" ] (select ())

                    selectTag {
                        option (value = "") { "Any entity" }
                        for entityType in auditEntityTypes do
                            UiHtml.optionTag entityType entityType (selected entityType filters.EntityType)
                    }
                }

                field
                    "From"
                    (UiHtml.attrs [ "type", "date"; "name", "fromDate"; "value", (filters.FromDate |> Option.defaultValue String.Empty) ] (input ()))
                    None

                field
                    "To"
                    (UiHtml.attrs [ "type", "date"; "name", "toDate"; "value", (filters.ToDate |> Option.defaultValue String.Empty) ] (input ()))
                    None

                if filters.Scope = Some "app" then
                    label (class' = "field") {
                        span () { "Run events" }
                        let includeValue = if filters.IncludeRunEvents then "true" else "false"
                        let selectTag = UiHtml.attrs [ "name", "includeRunEvents" ] (select ())

                        selectTag {
                            UiHtml.optionTag "true" "Include run events" (includeValue = "true")
                            UiHtml.optionTag "false" "Only app events" (includeValue = "false")
                        }
                    }
            }

            div (class' = "form-actions") {
                a (href = clearHref, class' = "button button-ghost") { "Clear filters" }
                button (type' = "submit", class' = "button") { "Apply filters" }
            }
        }

    let auditPage (users: UserData list) (events: EnhancedEventData list) (page: int) (total: int) (scope: string option) (baseHref: string) (filters: AuditFilterValues) =
        let totalPages = max 1 ((total + UiModels.PageSize - 1) / UiModels.PageSize)
        let pageHref targetPage =
            let separator = if baseHref.Contains "?" then "&" else "?"
            $"{baseHref}{separator}page={targetPage}"

        section (class' = "stack") {
            section (class' = "card") {
                div (class' = "toolbar") {
                    div () { cardHeader (auditScopeTitle filters.Scope) (Some $"{total} events · Page {page} of {totalPages}") }
                    a (href = baseHref, class' = "button button-ghost") { "Refresh" }
                }
                p () { scope |> Option.defaultValue "Showing all audit events." }
            }

            section (class' = "card") {
                cardHeader "Filters" (Some "Narrow by actor, event/entity type, date range, and text search.")
                auditFilterForm users filters
            }

            if List.isEmpty events then
                emptyState "No audit events found" "Adjust the filters or clear search to see more events."
            else
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
            spaceSectionBreadcrumb space "Trash"
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
