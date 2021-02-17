module FSharp.Control.Futures.Tests.Bridge

open System
open FSharp.Control.Futures
open Expecto
open FSharp.Control.Futures.Channels

// TODO: Add tests specific for OneShotChannel

let bridgeSend = test "Bridge send with receive" {
    let ch = Bridge.create ()
    ch.Send(1)
    ch.Send(2)
    ch.Dispose()

    let x1 = ch.Receive() |> Future.run
    let x2 = ch.Receive() |> Future.run
    let x3 = ch.Receive() |> Future.run

    Expect.equal x1 (Ok 1) "Error on receive first msg"
    Expect.equal x2 (Ok 2) "Error on receive second msg"
    Expect.equal x3 (Error Closed) "Error on receive third msg"
}

let bridgeDoubleReceiveEmptyWithoutAwait = test "Bridge double receive without await exception" {
    let ch = Bridge.create ()

    ch.Receive() |> ignore

    Expect.throws (fun () -> ch.Receive() |> ignore) "Don't throw exception on second receive"
    ch.Dispose()
}

let bridgeSecondReceiveFromClosed = test "Bridge double receive from closed channel" {
    let ch = Bridge.create ()
    ch.Send(1)
    ch.Dispose()

    let x1 = ch.Receive() |> Future.run
    let x2 = ch.Receive() |> Future.run
    let x3 = ch.Receive() |> Future.run

    Expect.equal x1 (Ok 1) "Error on receive first msg"
    Expect.equal x2 (Error Closed) "Error on receive second msg"
    Expect.equal x3 (Error Closed) "Error on receive third msg"
}

[<Tests>]
let tests =
    testList "Bridge" [
        bridgeSend
        bridgeSecondReceiveFromClosed
        bridgeDoubleReceiveEmptyWithoutAwait
    ]
