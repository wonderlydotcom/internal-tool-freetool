namespace Freetool.Api.Ui

open System
open Oxpecker.ViewEngine

module LayoutView =
    let private stylesheet href =
        UiHtml.attrs [ "rel", "stylesheet"; "href", href ] (link ())

    let private scriptModule src =
        let tag = UiHtml.attrs [ "type", "module"; "src", src ] (script ())
        tag { "" }

    let private navLink (active: bool) (href: string) (labelText: string) : HtmlElement =
        let attrs =
            [ "href", href; "class", if active then "nav-link is-active" else "nav-link" ]
            @ if active then [ "aria-current", "page" ] else []

        let tag = UiHtml.attrs attrs (a ())
        tag { labelText }

    let flash (message: FlashMessage option) : HtmlElement = section (id = "status-strip", class' = "status-strip") {
        match message with
        | None -> ()
        | Some flash ->
            let className = $"flash flash-{UiModels.flashClass flash}"
            p (class' = className) { flash.Message }
    }

    let private devBanner (model: LayoutModel) : HtmlElement =
        if not model.IsDevMode then
            div () { }
        else
            div (class' = "dev-banner") {
                strong () { "Development mode" }

                span () {
                    match model.CurrentUserEmail with
                    | Some email -> $"Signed in as {email}"
                    | None -> "No dev user selected"
                }

                let switchHref =
                    $"/dev/select-user?returnUrl={Uri.EscapeDataString(model.ReturnUrl)}"

                let switchLink =
                    UiHtml.attrs [ "href", switchHref; "class", "button button-small" ] (a ())

                switchLink { "Switch user" }
            }

    let private expressionEditorModal: HtmlElement =
        let editor =
            UiHtml.attrs
                [
                    "id", "template-expression-input"
                    "rows", "5"
                    "placeholder", "Type @ to insert variables, e.g. @Debit ? -1 * @Amount : @Amount"
                    "data-template-input", "true"
                    "data-expression-editor-input", "true"
                    "data-disable-expression-modal", "true"
                    "aria-describedby", "template-expression-help"
                ]
                (textarea ())

        dialog (id = "template-expression-modal", class' = "modal expression-modal") {
            section (class' = "modal-card") {
                header (class' = "modal-header") {
                    div () {
                        h2 (id = "template-expression-title") { "Insert Expression" }

                        p () {
                            "Write a JavaScript-like expression or JSON object/array. Use @VariableName to reference inputs."
                        }
                    }

                    let closeButton =
                        UiHtml.attrs
                            [
                                "type", "button"
                                "class", "modal-close"
                                "data-expression-modal-cancel", "true"
                                "aria-label", "Close"
                            ]
                            (button ())

                    closeButton { "×" }
                }

                div (class' = "modal-scroll") {
                    label (class' = "field") {
                        span () { "Expression" }
                        editor { "" }
                    }

                    let validation =
                        UiHtml.attrs
                            [
                                "class", "expression-validation"
                                "data-expression-validation", "true"
                                "aria-live", "polite"
                            ]
                            (div ())

                    validation { "Expression cannot be empty" }

                    div (id = "template-expression-help", class' = "expression-help") {
                        p (class' = "expression-help-title") { "Supported operations" }

                        ul () {
                            li () {
                                raw
                                    "Arithmetic: <code>+</code>, <code>-</code>, <code>*</code>, <code>/</code>, <code>%</code>"
                            }

                            li () {
                                raw
                                    "Comparison: <code>==</code>, <code>!=</code>, <code>&lt;</code>, <code>&gt;</code>, <code>&lt;=</code>, <code>&gt;=</code>"
                            }

                            li () { raw "Logical: <code>&amp;&amp;</code>, <code>||</code>, <code>!</code>" }
                            li () { raw "Ternary: <code>condition ? valueIfTrue : valueIfFalse</code>" }

                            li () {
                                raw
                                    "JSON: <code>{ &quot;key&quot;: @Var }</code> or <code>[ @Var, 1 ]</code>; use variables as JSON values."
                            }
                        }
                    }

                    footer (class' = "modal-footer") {
                        let cancelButton =
                            UiHtml.attrs
                                [
                                    "type", "button"
                                    "class", "button button-ghost"
                                    "data-expression-modal-cancel", "true"
                                ]
                                (button ())

                        cancelButton { "Cancel" }

                        let saveButton =
                            UiHtml.attrs
                                [
                                    "type", "button"
                                    "class", "button"
                                    "data-expression-modal-save", "true"
                                    "disabled", "disabled"
                                ]
                                (button ())

                        saveButton { "Insert" }
                    }
                }
            }
        }

    let page (model: LayoutModel) (content: HtmlElement) : HtmlElement =
        let root = UiHtml.attrs [ "lang", "en" ] (html ())

        root {
            head () {
                UiHtml.attrs [ "charset", "utf-8" ] (meta ())
                UiHtml.attrs [ "name", "viewport"; "content", "width=device-width, initial-scale=1" ] (meta ())
                title () { model.Title }
                UiHtml.attrs [ "rel", "icon"; "href", "/favicon.ico" ] (link ())
                stylesheet "/css/app.css"
                scriptModule "/vendor/datastar/datastar.js"
                scriptModule "/js/app.js"
            }

            body () {
                devBanner model

                div (class' = "app-shell") {
                    aside (class' = "sidebar") {
                        a (href = "/spaces", class' = "brand") {
                            span (class' = "brand-mark") { "F" }
                            span () { "Freetool" }
                        }

                        nav (class' = "sidebar-nav") {
                            navLink (model.Active = "spaces") "/spaces" "Spaces"
                            navLink (model.Active = "users") "/users" "Users"
                            navLink (model.Active = "audit") "/audit" "Audit"
                        }

                        div (class' = "sidebar-footer") {
                            p (class' = "eyebrow") { "Signed in" }
                            strong () { model.CurrentUserName |> Option.defaultValue "Not signed in" }
                            small () { model.CurrentUserEmail |> Option.defaultValue "" }
                        }
                    }

                    main (class' = "main") {
                        header (class' = "page-header") {
                            div () {
                                p (class' = "eyebrow") { "Internal tools builder" }
                                h1 () { model.Title }
                            }
                        }

                        flash model.Flash
                        content
                    }
                }

                expressionEditorModal
            }
        }

    let statusPage title message details statusCode =
        let tone =
            if statusCode >= 500 then
                FlashTone.Error
            else
                FlashTone.Info

        let model = {
            Active = ""
            Title = title
            CurrentUserName = None
            CurrentUserEmail = None
            IsDevMode = UiContext.isDevMode ()
            DevUsers = []
            ReturnUrl = "/spaces"
            Flash = Some { Tone = tone; Message = message }
        }

        page
            model
            (section (class' = "card") {
                h2 () { title }
                p () { message }

                match details with
                | Some text -> pre (class' = "code-block") { code () { text } }
                | None -> ()

                a (href = "/spaces", class' = "button") { "Go to spaces" }
            })