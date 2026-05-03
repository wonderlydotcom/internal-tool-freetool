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
                            navLink (model.Active = "spaces-list") "/spaces-list" "Spaces List"
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