module FSharp.Control.Futures.Tests.OneShot

open System
open FSharp.Control.Futures
open Expecto
open FSharp.Control.Futures.Channels


let oneshotOneSend = test "OneShot one sync send correct" {
    use ch = OneShot.create ()
    ch.SendImmediate(12)
    let x = ch.PollReceive()

    Expect.notEqual x Poll.Pending "Sent value is not received"
    Expect.equal x (Poll.Ready 12) "Sent value not equal received value"
}

let oneshotDoubleSend = test "OneShot double send exception" {
    use ch = OneShot.create ()
    ch.SendImmediate(1)
    Expect.throws (fun () -> ch.SendImmediate(2)) "Don't throw exception on second send"
}

let oneshotReceiveFromInterrupted = test "OneShot receive from interrupted channel" {
    let ch = OneShot.create ()
    (ch :> IDisposable).Dispose()
    Expect.throws (fun () -> ch.PollReceive() |> ignore) "Receive from interrupted channel is not throws"
}

let oneshotOneSendAsync = test "OneShot one sync send correct async" {
    let s, r = OneShot.createPair ()
    s.Send(12) |> Future.run
    let x = r.Receive() |> Future.run
    s.Dispose()
    Expect.isTrue (x.IsSome) "Sent value is not received"
    Expect.equal x (Some 12) "Sent value not equal received value"
}

let oneshotOneSendAsyncReceiveAfterDispose = test "OneShot one send correct async with receive after dispose" {
    let s, r = OneShot.createPair ()
    s.Send(12) |> Future.run
    s.Dispose()
    let x = r.Receive() |> Future.run
    Expect.isTrue (x.IsSome) "Sent value is not received"
    Expect.equal x (Some 12) "Sent value not equal received value"
}

let oneshotDoubleSendAsync = test "OneShot double send exception async" {
    let s, _ = OneShot.createPair ()
    s.Send(1) |> Future.run
    s.Dispose()
    Expect.throws (fun () -> s.Send(2) |> Future.run) "Don't throw exception on second send"
}

let oneshotReceiveFromInterruptedAsync = test "OneShot receive from interrupted channel async" {
    let ch = OneShot.create ()
    (ch :> IDisposable).Dispose()
    let x = (ch :> IReceiver<_>).Receive() |> Future.run
    Expect.equal x None "Receive from interrupted channel is not None"
}

[<Tests>]
let tests =
    testList "OneShot" [
        oneshotOneSend
        oneshotDoubleSend
        oneshotReceiveFromInterrupted
        oneshotOneSendAsync
        oneshotOneSendAsyncReceiveAfterDispose
        oneshotDoubleSendAsync
        oneshotReceiveFromInterruptedAsync
    ]
