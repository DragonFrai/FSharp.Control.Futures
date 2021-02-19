module FSharp.Control.Futures.Tests.Bridge

open System
open FSharp.Control.Futures
open Expecto
open FSharp.Control.Futures.SeqStream.Channels
open FSharp.Control.Futures.SeqStream

// TODO: Add tests specific for OneShotChannel

let bridgeSend = test "Bridge send with pollNext" {
    let ch = Bridge.create ()
    ch.Send(1)
    ch.Send(2)
    ch.Dispose()

    let x1 = ch.PollNext(noCallableWaker)
    let x2 = ch.PollNext(noCallableWaker)
    let x3 = ch.PollNext(noCallableWaker)

    Expect.equal x1 (SeqNext 1) "Error on receive first msg"
    Expect.equal x2 (SeqNext 2) "Error on receive second msg"
    Expect.equal x3 (SeqCompleted) "Error on receive third msg"
}

let bridgeSecondReceiveFromClosed = test "Bridge double pollNext from closed channel" {
    let ch = Bridge.create ()
    ch.Send(1)
    ch.Dispose()

    let x1 = ch.PollNext(noCallableWaker)
    let x2 = ch.PollNext(noCallableWaker)
    let x3 = ch.PollNext(noCallableWaker)

    Expect.equal x1 (SeqNext 1) "Error on receive first msg"
    Expect.equal x2 (SeqCompleted) "Error on receive second msg"
    Expect.equal x3 (SeqCompleted) "Error on receive third msg"
}

[<Tests>]
let tests =
    testList "Bridge" [
        bridgeSend
        bridgeSecondReceiveFromClosed
    ]
