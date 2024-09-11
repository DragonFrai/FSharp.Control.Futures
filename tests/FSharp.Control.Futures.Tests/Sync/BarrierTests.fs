module FSharp.Control.Futures.Tests.Sync.BarrierTests

open System.Collections.Generic
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync
open Xunit


[<Fact>]
let ``Barrier with 0 barrierLimit not block``() =
    let b = Barrier(0)
    do
        let f = NaiveFuture(b.Wait())
        Assert.Equal(NaivePoll.Ready(true), f.Poll(Context.nonAwakenedContext))
    do
        let f = NaiveFuture(b.Wait())
        Assert.Equal(NaivePoll.Ready(true), f.Poll(Context.nonAwakenedContext))
    ()


[<Fact>]
let ``Barrier with 1 barrierLimit not block``() =
    let b = Barrier(1)
    do
        let f = NaiveFuture(b.Wait())
        Assert.Equal(NaivePoll.Ready(true), f.Poll(Context.nonAwakenedContext))
    do
        let f = NaiveFuture(b.Wait())
        Assert.Equal(NaivePoll.Ready(true), f.Poll(Context.nonAwakenedContext))
    do
        let f = NaiveFuture(b.Wait())
        Assert.Equal(NaivePoll.Ready(true), f.Poll(Context.nonAwakenedContext))
    ()


[<Fact>]
let ``Barrier with 2 barrierLimit``() =
    let b = Barrier(2)

    let ft1 = TestFutureTask(b.Wait())

    do
        Assert.Equal(NaivePoll.Pending, ft1.Poll())

    let ft2 = TestFutureTask(b.Wait())

    let r2 = ft2.Poll()
    let r1 = ft1.Poll()

    Assert.True(r1 = NaivePoll.Ready true || r2 = NaivePoll.Ready true)
    Assert.False(r1 = NaivePoll.Ready true && r2 = NaivePoll.Ready true)

    ()

[<Fact>]
let ``Multiple barrier pass``() =
    let passes = 10
    let limit = 100

    let b = Barrier(limit)

    for _pass in 1..passes do
        let fTasks = List<TestFutureTask<'a>>()
        for _i in 1..(limit - 1) do
            let fTask = TestFutureTask(b.Wait())
            Assert.Equal(NaivePoll.Pending, fTask.Poll())
            fTasks.Add(fTask)

        for fTask in fTasks do
            Assert.Equal(NaivePoll.Pending, fTask.Poll(false))

        // pass barrier
        let finalFTask = TestFutureTask(b.Wait())
        let finalResult = finalFTask.Poll()

        let allResults = finalResult :: (fTasks |> Seq.map (_.Poll()) |> Seq.toList)

        for res in allResults do Assert.True(NaivePoll.isReady res)
        Assert.Equal(1, allResults |> Seq.filter (fun p -> p = NaivePoll.Ready true) |> Seq.length)
