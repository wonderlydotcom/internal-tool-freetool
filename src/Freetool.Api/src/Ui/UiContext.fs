namespace Freetool.Api.Ui

open System
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Freetool.Api.Auth
open Freetool.Application.Interfaces
open Freetool.Domain.ValueObjects

module UiContext =
    let currentUserOption (ctx: HttpContext) = RequestUserContext.tryGet ctx

    let currentUser (ctx: HttpContext) = RequestUserContext.get ctx

    let actorUserId (ctx: HttpContext) : UserId =
        match (currentUser ctx).UserId with
        | Some userId -> userId
        | None -> invalidOp "Authenticated UI request did not have a user id."

    let actorUserIdString ctx = (actorUserId ctx).Value.ToString()

    let actorEmail ctx = (currentUser ctx).Email

    let isDevMode () =
        String.Equals(
            Environment.GetEnvironmentVariable("FREETOOL_DEV_MODE"),
            "true",
            StringComparison.OrdinalIgnoreCase
        )

    let isDatastarRequest (ctx: HttpContext) =
        ctx.Request.Headers["Datastar-Request"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase)

    let antiforgeryToken (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx).RequestToken
        |> Option.ofObj
        |> Option.defaultValue String.Empty

    let validateAntiforgery (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx)

    let requestPathAndQuery (ctx: HttpContext) =
        ctx.Request.PathBase.ToString()
        + ctx.Request.Path.ToString()
        + ctx.Request.QueryString.ToString()

    let returnUrl (ctx: HttpContext) =
        let value = requestPathAndQuery ctx
        if String.IsNullOrWhiteSpace value then "/spaces" else value

    let private userRepository (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<IUserRepository>()

    let devUsers (ctx: HttpContext) = task {
        if not (isDevMode ()) then
            return []
        else
            let! users = (userRepository ctx).GetAllAsync 0 500
            return users |> List.map (fun user -> user.State)
    }

    let layoutModel (ctx: HttpContext) active title flash = task {
        let requestUser = currentUserOption ctx
        let! devUsers = devUsers ctx

        return {
            Active = active
            Title = title
            CurrentUserName = requestUser |> Option.bind (fun user -> user.Name)
            CurrentUserEmail = requestUser |> Option.map (fun user -> user.Email)
            IsDevMode = isDevMode ()
            DevUsers = devUsers
            ReturnUrl = returnUrl ctx
            Flash = flash
        }
    }