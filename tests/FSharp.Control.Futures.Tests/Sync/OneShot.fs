module FSharp.Control.Futures.Tests.Sync.OneShot

open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync
open Xunit


[<Fact>]
let ``Send, receive`` () =
    let tx, rx = OneShot().AsPair
    let fTask = spawn (rx.Receive())

    Assert.Equal(NaivePoll.Pending, fTask.Poll())
    Assert.True(tx.Send(12))
    Assert.Equal(NaivePoll.Ready 12, fTask.Poll())

[<Fact>]
let ``Drop rx`` () =
    let tx, rx = OneShot().AsPair
    let fTask = spawn (rx.Receive())

    Assert.Equal(NaivePoll.Pending, fTask.Poll())
    fTask.Drop()

    Assert.False(tx.Send(12))

[<Fact>]
let ``Close rx`` () =
    let tx, rx = OneShot().AsPair
    rx.Close()
    Assert.False(tx.Send(12))
