module Freetool.Domain.Tests.DomainPropertyTests

open System
open FsCheck
open Freetool.Domain
open Xunit

[<Fact>]
let ``ValidationError preserves arbitrary message text`` () =
    let property (message: string) =
        let expected = if isNull message then String.Empty else message

        match (ValidationError expected: DomainError) with
        | ValidationError actual -> actual = expected
        | _ -> false

    Check.QuickThrowOnFailure property