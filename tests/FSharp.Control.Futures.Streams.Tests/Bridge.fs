module FSharp.Control.Futures.Streams.Tests.Bridge

open Expecto
open FSharp.Control.Futures.Streams.Channels
open FSharp.Control.Futures.Streams


let bridgeSend = test "Bridge send with pollNext" {
    let ch = Bridge.create ()
    ch.Send(1)
    ch.Send(2)
    ch.Dispose()

    let x1 = ch.PollNext(nonAwakenedContext)
    let x2 = ch.PollNext(nonAwakenedContext)
    let x3 = ch.PollNext(nonAwakenedContext)

    Expect.equal x1 (StreamPoll.Next 1) "Error on receive first msg"
    Expect.equal x2 (StreamPoll.Next 2) "Error on receive second msg"
    Expect.equal x3 (StreamPoll.Completed) "Error on receive third msg"
}

let bridgeSecondReceiveFromClosed = test "Bridge double pollNext from closed channel" {
    let ch = Bridge.create ()
    ch.Send(1)
    ch.Dispose()

    let x1 = ch.PollNext(nonAwakenedContext)
    let x2 = ch.PollNext(nonAwakenedContext)
    let x3 = ch.PollNext(nonAwakenedContext)

    Expect.equal x1 (StreamPoll.Next 1) "Error on receive first msg"
    Expect.equal x2 (StreamPoll.Completed) "Error on receive second msg"
    Expect.equal x3 (StreamPoll.Completed) "Error on receive third msg"
}

[<Tests>]
let tests =
    testList "Bridge" [
        bridgeSend
        bridgeSecondReceiveFromClosed
    ]
