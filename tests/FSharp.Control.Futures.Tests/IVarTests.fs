module FSharp.Control.Futures.Tests.IVarTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


let ivarPut = test "IVar put and await sync" {
    let ivar = IVar<int>()

    ivar.Put(12)
    let x = ivar |> Future.runSync

    Expect.equal x 12 "IVar return illegal value"
    ()
}

[<Tests>]
let tests =
    testList "IVar" [
        ivarPut
    ]
