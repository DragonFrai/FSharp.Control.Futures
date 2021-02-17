module FSharp.Control.Futures.Tests.OneShot

open System
open FSharp.Control.Futures
open Expecto
open FSharp.Control.Futures.Channels

// TODO: Add tests specific for OneShotChannel

let oneshotOneSend = test "OneShot one sync send correct" {
    use ch = OneShot.create ()
    ch.Send(12)
    let x = ch.Receive() |> Future.run

    Expect.notEqual x (Error Closed) "Sent value is not received"
    Expect.equal x (Ok 12) "Sent value not equal received value"
}

let oneshotDoubleSend = test "OneShot double send exception" {
    use ch = OneShot.create ()
    ch.Send(1)
    Expect.throws (fun () -> ch.Send(2)) "Don't throw exception on second send"
}

let oneshotReceiveFromClosed = test "OneShot receive from interrupted channel" {
    let ch = OneShot.create ()
    ch.Send(1)
    ch.Dispose()
    Expect.equal (Ok 1) (ch.Receive() |> Future.run) "Receive from interrupted channel is not return value"
    Expect.equal (Error Closed) (ch.Receive() |> Future.run) "Second receive from interrupted channel is not return value"
}

[<Tests>]
let tests =
    testList "OneShot" [
        oneshotOneSend
        oneshotDoubleSend
        oneshotReceiveFromClosed
    ]
