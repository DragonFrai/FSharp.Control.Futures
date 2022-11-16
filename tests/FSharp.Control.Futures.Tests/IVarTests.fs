module FSharp.Control.Futures.Tests.IVarTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Sync


let ivarWriteBeforeRead = test "IVar write before read" {
    let ivar = IVar<int>()

    IVar.writeValue 12 ivar
    let x = ivar.Read() |> Future.runSync

    Expect.equal x 12 "IVar return illegal value"
    ()
}

let ivarReadBeforeWrite = test "IVar read before write" {
    let ivar = IVar<int>()
    let readFut = IVar.read ivar
    let writeFut = Future.lazy' (fun () -> IVar.writeValue 12 ivar)

    let fut = Future.merge readFut writeFut

    let x, _ = fut |> Future.runSync

    Expect.equal x 12 "IVar return illegal value"
    ()
}

[<Tests>]
let tests =
    testList "IVar" [
        ivarWriteBeforeRead
        ivarReadBeforeWrite
    ]
