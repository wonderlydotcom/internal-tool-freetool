namespace Freetool.Api.Ui

open Freetool.Domain.Entities

[<RequireQualifiedAccess>]
type FlashTone =
    | Success
    | Error
    | Info

type FlashMessage = { Tone: FlashTone; Message: string }

type LayoutModel = {
    Active: string
    Title: string
    CurrentUserName: string option
    CurrentUserEmail: string option
    IsDevMode: bool
    DevUsers: UserData list
    ReturnUrl: string
    Flash: FlashMessage option
}

module UiModels =
    [<Literal>]
    let PageSize = 50

    [<Literal>]
    let DevUserCookieName = "freetool_dev_user_id"

    let flashClass flash =
        match flash.Tone with
        | FlashTone.Success -> "success"
        | FlashTone.Error -> "error"
        | FlashTone.Info -> "info"