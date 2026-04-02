namespace Freetool.Api.Controllers

open Freetool.Api.Auth
open Microsoft.AspNetCore.Mvc
open Freetool.Domain.ValueObjects

[<ApiController>]
type AuthenticatedControllerBase() =
    inherit ControllerBase()

    member this.CurrentUserId: UserId =
        match
            RequestUserContext.tryGet this.HttpContext
            |> Option.bind (fun requestUser -> requestUser.UserId)
        with
        | Some userId -> userId
        | None ->
            match this.HttpContext.Items.TryGetValue "UserId" with
            | true, userIdObj when userIdObj <> null -> userIdObj :?> UserId
            | _ ->
                invalidOp
                    "User not authenticated. This indicates a middleware configuration error - authentication middleware should have rejected unauthenticated requests."

    [<NonAction>]
    member this.TryGetCurrentUserId() : UserId option =
        match
            RequestUserContext.tryGet this.HttpContext
            |> Option.bind (fun requestUser -> requestUser.UserId)
        with
        | Some userId -> Some userId
        | None ->
            match this.HttpContext.Items.TryGetValue "UserId" with
            | true, userIdObj when userIdObj <> null -> Some(userIdObj :?> UserId)
            | _ -> None