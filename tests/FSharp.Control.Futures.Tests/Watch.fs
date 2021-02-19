module FSharp.Control.Futures.Tests.Watch

open System
open FSharp.Control.Futures
open Expecto
open FSharp.Control.Futures.Channels

// TODO: Add tests specific for OneShotChannel

let watchOneSend = test "Watch one send" {
    use ch = Watch.create ()

    ch.Send(12)
    let x = ch.PollNext(noCallableWaker)

    Expect.equal x (SeqNext 12) "Sent value not equal polled received] value"
}

let watchDoubleSend = test "Watch double send" {
    use ch = Watch.create ()

    // Send - Poll
    ch.Send(1)
    Expect.equal (ch.PollNext(noCallableWaker)) (SeqNext 1) "Sent value not equal polled value"
    ch.Send(2)
    Expect.equal (ch.PollNext(noCallableWaker)) (SeqNext 2) "Sent value not equal polled value"
}

let watchReceiveFromClosed = test "Watch poll closed channel" {
    let ch = Watch.create ()
    ch.Send(1)
    ch.Dispose()
    Expect.equal (ch.PollNext(noCallableWaker)) (SeqNext 1)  "Poll from closed watch channel is not return last value"
    Expect.equal (ch.PollNext(noCallableWaker)) (SeqCompleted)  "Poll from closed watch channel is not return completed"
}

let watchDoubleSendWatchLast = test "Watch poll last" {
    use ch = Watch.create ()
    // Poll last
    ch.Send(1)
    ch.Send(2)
    Expect.equal (ch.PollNext(noCallableWaker)) (SeqNext 2) "Polled value is not last"
}

[<Tests>]
let tests =
    testList "Watch" [
        watchOneSend
        watchDoubleSend
        watchReceiveFromClosed
        watchDoubleSendWatchLast
    ]
