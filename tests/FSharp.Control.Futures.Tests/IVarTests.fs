module FSharp.Control.Futures.Tests.IVarTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Sync


let ivarPut = test "IVar put and await sync" {
    let ivar = IVar<int>()

    IVar.writeValue 12 ivar
    let x = ivar.Read() |> Future.runSync

    Expect.equal x 12 "IVar return illegal value"
    ()
}

[<Tests>]
let tests =
    testList "IVar" [
        ivarPut
    ]
