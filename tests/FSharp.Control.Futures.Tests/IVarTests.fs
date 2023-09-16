module FSharp.Control.Futures.Tests.IVarTests

open System
open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Types
open FSharp.Control.Futures.Sync


let ivarWriteBeforeRead = test "IVar write before read" {
    let ivar = IVar<int>()

    IVar.put 12 ivar
    let x = ivar.Get() |> Future.runSync

    Expect.equal x 12 "IVar return illegal value"
    ()
}

let ivarReadBeforeWrite = test "IVar read before write" {
    let ivar = IVar<int>()
    let readFut = IVar.get ivar
    let writeFut = Future.lazy' (fun () -> IVar.put 12 ivar)

    let fut = Future.merge readFut writeFut

    let x, _ = fut |> Future.runSync

    Expect.equal x 12 "IVar return illegal value"
    ()
}

let ivarReadExn = test "IVar read exn" {
    let ivar = IVar<int>()
    let readFut = IVar.get ivar
    let writeFut = Future.lazy' (fun () -> IVar.putExn (Exception("error")) ivar)

    let fut = Future.merge readFut writeFut

    try
        let x, _ = fut |> Future.runSync
        failwith "Error not writed"
    with
    | e ->
        ()
}

[<Tests>]
let tests =
    testList "IVar" [
        ivarWriteBeforeRead
        ivarReadBeforeWrite
        ivarReadExn
    ]
