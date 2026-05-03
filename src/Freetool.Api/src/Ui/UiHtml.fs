namespace Freetool.Api.Ui

open Oxpecker.ViewEngine

module UiHtml =
    let attrs (attributes: (string * string) list) (tag: #HtmlTag) =
        for name, value in attributes do
            tag.attr (name, value) |> ignore

        tag

    let attr name value tag = attrs [ name, value ] tag

    let whenAttr enabled name value = if enabled then [ name, value ] else []

    let selectedAttr selected = whenAttr selected "selected" "selected"

    let checkedAttr selected = whenAttr selected "checked" "checked"

    let disabledAttr disabled = whenAttr disabled "disabled" "disabled"

    let requiredAttr required = whenAttr required "required" "required"

    let antiforgeryInput (token: string) : HtmlElement =
        attrs [ "type", "hidden"; "name", "__RequestVerificationToken"; "value", token ] (input ())

    let hidden (name: string) (value: string) : HtmlElement = attrs [ "type", "hidden"; "name", name; "value", value ] (input ())

    let textInput (name: string) (value: string) (required: bool) (placeholder: string) : HtmlElement =
        attrs
            ([ "type", "text"; "name", name; "value", value; "placeholder", placeholder ] @ requiredAttr required)
            (input ())

    let textareaInput (name: string) (value: string) (rows: int) : HtmlElement =
        let tag = attrs [ "name", name; "rows", string rows ] (textarea ())
        tag { value }

    let checkbox (name: string) (value: string) (isChecked: bool) : HtmlElement =
        attrs ([ "type", "checkbox"; "name", name; "value", value ] @ checkedAttr isChecked) (input ())

    let optionTag (value: string) (label: string) (selected: bool) : HtmlElement =
        let tag = attrs ([ "value", value ] @ selectedAttr selected) (option ())
        tag { label }

    let datastarGet url = $"@get('{url}')"

    let datastarPostForm url = $"@post('{url}', {{contentType: 'form'}})"

    let formAttrs method action extra = [ "method", method; "action", action ] @ extra

    let enhancedPostAttrs action extra =
        formAttrs "post" action ([ "data-on:submit__prevent", datastarPostForm action ] @ extra)

    let enhancedPostForm (action: string) (extra: (string * string) list) = attrs (enhancedPostAttrs action extra) (form ())

    let linkButton (href: string) (labelText: string) : HtmlElement =
        let tag = attrs [ "href", href; "class", "button button-secondary" ] (a ())
        tag { labelText }

    let submitButton (labelText: string) : HtmlElement = button (type' = "submit", class' = "button") { labelText }

    let dangerSubmitButton (labelText: string) : HtmlElement =
        button (type' = "submit", class' = "button button-danger") { labelText }
