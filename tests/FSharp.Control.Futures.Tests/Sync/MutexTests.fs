module FSharp.Control.Futures.Tests.Sync.MutexTests

open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync
open Xunit


[<Fact>]
let ``Sequential locks``() =
    let mutable d = 0
    let m = Mutex()

    do
        let lockFTask = TestFutureTask(m.Lock())
        Assert.Equal(NaivePoll.Ready (), lockFTask.Poll())
        Assert.Equal(0, d)
        d <- 1
        m.Unlock()

    do
        let lockFTask = TestFutureTask(m.Lock())
        Assert.Equal(NaivePoll.Ready (), lockFTask.Poll())
        Assert.Equal(1, d)
        d <- 2
        m.Unlock()

    do
        let lockFTask = TestFutureTask(m.Lock())
        Assert.Equal(NaivePoll.Ready (), lockFTask.Poll())
        Assert.Equal(2, d)
        m.Unlock()

[<Fact>]
let ``Parallel lock`` () =
    let m = Mutex()

    let fTask1 = m.Lock() |> TestFutureTask
    let fTask2 = m.Lock() |> TestFutureTask

    Assert.Equal(NaivePoll.Ready (), fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())

    m.Unlock() // Unlock task 1 lock
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())
    m.Unlock() // Unlock task 2 lock

[<Fact>]
let ``Try lock`` () =
    let m = Mutex()

    Assert.True(m.TryLock())
    Assert.False(m.TryLock())
    m.Unlock()
    Assert.True(m.TryLock())

[<Fact>]
let ``Lock future not take lock if dropped`` () =
    let m = Mutex()

    let fTask1 = m.Lock() |> TestFutureTask
    let fTask2 = m.Lock() |> TestFutureTask
    let fTask3 = m.Lock() |> TestFutureTask

    Assert.Equal(NaivePoll.Ready (), fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())
    m.Unlock() // Unlock task 1 lock

    fTask2.Drop()
    // Assert that task 3 take lock after task 2 dropped until wait locking
    Assert.Equal(NaivePoll.Ready (), fTask3.Poll())
    m.Unlock() // Unlock task 3 lock
