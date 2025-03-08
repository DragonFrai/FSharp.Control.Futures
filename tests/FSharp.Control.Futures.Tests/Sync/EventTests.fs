module FSharp.Control.Futures.Tests.Sync.EventTests

open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync
open Xunit


[<Fact>]
let ``Wait Set Poll``() =
    let ev = Event()
    let waitFTask = mkTestFutureTask (ev.Wait())
    ev.Set()
    Assert.Equal(NaivePoll.Ready (), waitFTask.Poll())

[<Fact>]
let ``Wait Poll Set``() =
    let ev = Event()
    let waitFTask = mkTestFutureTask (ev.Wait())
    Assert.Equal(NaivePoll.Pending, waitFTask.Poll())
    ev.Set()
    Assert.Equal(NaivePoll.Ready (), waitFTask.Poll())

[<Fact>]
let ``Set Wait Poll``() =
    let ev = Event()
    ev.Set()
    let waitFTask = mkTestFutureTask (ev.Wait())
    Assert.Equal(NaivePoll.Ready (), waitFTask.Poll())

[<Fact>]
let ``Set wakes all queued tasks``() =
    let ev = Event()
    let waitTask1 = mkTestFutureTask (ev.Wait())
    let waitTask2 = mkTestFutureTask (ev.Wait())

    Assert.Equal(NaivePoll.Pending, waitTask1.Poll())
    Assert.Equal(NaivePoll.Pending, waitTask2.Poll())
    ev.Set()
    Assert.True(waitTask1.IsWaked)
    Assert.Equal(NaivePoll.Ready (), waitTask1.Poll())
    Assert.True(waitTask2.IsWaked)
    Assert.Equal(NaivePoll.Ready (), waitTask2.Poll())

[<Fact>]
let ``Dropped tasks dequeued``() =
    let ev = Event()
    let waitTask1 = mkTestFutureTask (ev.Wait())
    let waitTask2 = mkTestFutureTask (ev.Wait())

    Assert.Equal(NaivePoll.Pending, waitTask1.Poll())
    Assert.Equal(NaivePoll.Pending, waitTask2.Poll())
    do waitTask1.Drop()
    ev.Set()
    Assert.False(waitTask1.IsWaked)
    Assert.True(waitTask2.IsWaked)
    Assert.Equal(NaivePoll.Ready (), waitTask2.Poll())
