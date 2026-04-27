module Freetool.Domain.Tests.DomainPropertyTests

open System
open FsCheck
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Xunit

let private nonBlank fallback (value: string) =
    let candidate = if isNull value then fallback else value

    if String.IsNullOrWhiteSpace candidate then
        fallback
    else
        candidate

[<Fact>]
let ``DatabasePort accepts exactly valid TCP port values`` () =
    let property (port: int) =
        match DatabasePort.Create(Some port) with
        | Ok value -> port >= 1 && port <= 65535 && value.Value = port
        | Error(ValidationError _) -> port < 1 || port > 65535
        | _ -> false

    Check.QuickThrowOnFailure property

[<Fact>]
let ``HttpMethod creation is case-insensitive and canonicalizes output`` () =
    let property (rawIndex: int) =
        let methods = [ "delete"; "get"; "patch"; "post"; "put" ]
        let methodName = methods[abs (rawIndex % methods.Length)]

        match HttpMethod.Create methodName with
        | Ok method -> method.ToString() = methodName.ToUpperInvariant()
        | Error _ -> false

    Check.QuickThrowOnFailure property

[<Fact>]
let ``InputType Radio rejects duplicate generated option values`` () =
    let property (rawValue: string) =
        let value = nonBlank "choice" rawValue

        let options = [
            { Value = value; Label = Some "first" }
            { Value = value; Label = Some "second" }
        ]

        match InputType.Radio options with
        | Error(ValidationError _) -> true
        | _ -> false

    Check.QuickThrowOnFailure property

[<Fact>]
let ``Integer default values round-trip through typed defaults`` () =
    let property (value: int) =
        match DefaultValue.Create(InputType.Integer(), string value) with
        | Ok defaultValue -> defaultValue.ToRawString() = string value
        | Error _ -> false

    Check.QuickThrowOnFailure property

[<Fact>]
let ``BusinessRules report conflicts only for intersecting generated keys`` () =
    let property (rawKey: string) (rawOtherKey: string) =
        let key = nonBlank "shared" rawKey
        let otherKey = nonBlank "other" rawOtherKey

        let app = {
            AppId = "app-1"
            UrlParameters = [ key, "from-app" ]
            Headers = []
            Body = []
        }

        let conflict =
            BusinessRules.checkAppToResourceConflicts [ app ] (Some [ key, "from-resource" ]) None None

        let noConflict =
            BusinessRules.checkAppToResourceConflicts [ app ] (Some [ otherKey + "-resource", "value" ]) None None

        match conflict, noConflict with
        | Error(InvalidOperation message), Ok() -> message.Contains(key)
        | _ -> false

    Check.QuickThrowOnFailure property