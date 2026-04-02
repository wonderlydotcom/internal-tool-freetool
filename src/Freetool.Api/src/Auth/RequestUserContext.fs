namespace Freetool.Api.Auth

open Freetool.Domain.ValueObjects
open Microsoft.AspNetCore.Http

[<CLIMutable>]
type RequestUserContext = {
    UserId: UserId option
    Name: string option
    Email: string
    Profile: string option
    GroupKeys: string list
    AuthenticationSource: string
}

[<RequireQualifiedAccess>]
module RequestUserContext =
    [<Literal>]
    let ItemKey = "RequestUserContext"

    let set (context: HttpContext) (requestUser: RequestUserContext) = context.Items[ItemKey] <- requestUser

    let tryGet (context: HttpContext) =
        match context.Items.TryGetValue ItemKey with
        | true, (:? RequestUserContext as requestUser) -> Some requestUser
        | _ -> None

    let get (context: HttpContext) =
        tryGet context
        |> Option.defaultWith (fun () ->
            invalidOp $"Request user context was not available for {context.Request.Method} {context.Request.Path}.")