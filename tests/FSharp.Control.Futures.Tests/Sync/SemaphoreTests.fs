module FSharp.Control.Futures.Tests.Sync.SemaphoreTests

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync
open Xunit


// [ TryAcquire ]

[<Fact>]
let ``Try acquire (init with 0 permits)``() =
    let s = Semaphore(0)
    do Assert.False(s.TryAcquire())
    do s.Release()
    do Assert.True(s.TryAcquire())

[<Fact>]
let ``Try acquire (init with 1 permits)``() =
    let s = Semaphore(1)
    do Assert.True(s.TryAcquire())
    do Assert.False(s.TryAcquire())
    do s.Release()
    do Assert.True(s.TryAcquire())

[<Fact>]
let ``Try acquire many``() =
    let s = Semaphore(1)
    do Assert.False(s.TryAcquire(3))
    do s.Release(2)
    do Assert.True(s.TryAcquire(3))


// [ Acquire ]

[<Fact>]
let ``Acquire ready immediate``() =
    let s = Semaphore(1)
    let fTask = mkTestFutureTask (s.Acquire())
    Assert.Equal(NaivePoll.Ready (), fTask.Poll())
    ()

[<Fact>]
let ``Acquire ready immediate with Release``() =
    let s = Semaphore(0)
    let fTask = mkTestFutureTask (s.Acquire())
    s.Release()
    Assert.Equal(NaivePoll.Ready (), fTask.Poll())
    ()

[<Fact>]
let ``Acquire pending``() =
    let s = Semaphore(0)
    let fTask = mkTestFutureTask (s.Acquire())
    Assert.Equal(NaivePoll.Pending, fTask.Poll())
    s.Release(1)
    Assert.Equal(NaivePoll.Ready (), fTask.Poll(true))
    ()

[<Fact>]
let ``Acquire dropped acquire not take permits``() =
    let s = Semaphore(0)
    let fTask = mkTestFutureTask (s.Acquire())
    Assert.Equal(NaivePoll.Pending, fTask.Poll())
    s.Release(1)
    Assert.True(fTask.IsWaked)
    fTask.Drop()
    Assert.Equal(1, s.AvailablePermits)
    Assert.Equal(true, s.TryAcquire())
    Assert.Equal(0, s.AvailablePermits)
    ()

[<Fact>]
let ``Acquire next AcquireFuture waked on drop prev``() =
    let s = Semaphore(0)
    let fTask1 = mkTestFutureTask (s.Acquire())
    let fTask2 = mkTestFutureTask (s.Acquire())
    Assert.Equal(NaivePoll.Pending, fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())
    s.Release(1)
    Assert.True(fTask1.IsWaked)
    fTask1.Drop()
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll(true))
    ()


// [ Max permits ]

[<Fact>]
let ``Semaphore new max permits``() =
    let s = Semaphore(Semaphore.MaxPermits)
    Assert.Equal(Semaphore.MaxPermits, s.AvailablePermits)

[<Fact>]
let ``Semaphore add max permits (0 + max)``() =
    let s = Semaphore(0)
    s.Release(Semaphore.MaxPermits)
    Assert.Equal(Semaphore.MaxPermits, s.AvailablePermits)

[<Fact>]
let ``Semaphore add max permits ((max - 1) + 1)``() =
    let s = Semaphore(Semaphore.MaxPermits - 1)
    s.Release(1)
    Assert.Equal(Semaphore.MaxPermits, s.AvailablePermits)


// [ Overflows ]

[<Fact>]
let ``Semaphore new permits overflow``() =
    let _ex = Assert.ThrowsAny(fun () ->
        let _s = Semaphore(Semaphore.MaxPermits + 1)
        ()
    )
    ()

[<Fact>]
let ``Semaphore add permits overflow (1 + max)``() =
    let s = Semaphore(1)
    let _ex = Assert.ThrowsAny(fun () -> s.Release(Semaphore.MaxPermits))
    ()

[<Fact>]
let ``Semaphore add permits overflow (max + 1)``() =
    let s = Semaphore(Semaphore.MaxPermits)
    let _ex = Assert.ThrowsAny(fun () -> s.Release(1))
    ()


// [ Fifo ]

[<Fact>]
let ``Single permits fifo``() =
    let s = Semaphore(0)
    let fTask1 = mkTestFutureTask (s.Acquire())
    let fTask2 = mkTestFutureTask (s.Acquire())

    Assert.Equal(NaivePoll.Pending, fTask1.Poll())
    s.Release()
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())
    Assert.Equal(NaivePoll.Ready (), fTask1.Poll())

    ()

[<Fact>]
let ``Multiple permits fifo``() =
    let s = Semaphore(1)
    let fTask1 = mkTestFutureTask (s.Acquire(2))
    let fTask2 = mkTestFutureTask (s.Acquire(1))

    Assert.Equal(NaivePoll.Pending, fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())
    s.Release(1)
    Assert.Equal(NaivePoll.Ready (), fTask1.Poll())
    Assert.False(fTask2.IsWaked)
    s.Release(1)
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())


// [ Stress tests ]

[<Fact>]
let ``Semaphore counter stress test``() =
    let workers = 1000
    let perWorkerIterations = 1000
    let expectedResult = workers * perWorkerIterations

    let mutable counter = 0
    let semaphore = Semaphore(0)
    let mutable workerTasks = []
    for _ in 1..workers do
        let fTask = ThreadPoolRuntime.spawn (future {
            for _ in 1..perWorkerIterations do
                do! semaphore.Acquire()
                counter <- counter + 1
                do semaphore.Release()
                do! Future.yield' ()
        })
        workerTasks <- fTask :: workerTasks

    do semaphore.Release(1)
    for wTask in workerTasks do
        let r = wTask.Await() |> Future.runBlocking
        Assert.Equal(r, Ok ())

    Assert.Equal(expectedResult, counter)

[<Fact>]
let ``Semaphore stress test``() =
    let workers = 1000

    let barrier = Barrier(workers)
    let semaphore = Semaphore(5)

    let mutable workerTasks = []
    for _ in 1..workers do
        let fTask = ThreadPoolRuntime.spawn (future {
            do! barrier.Wait() |> Future.ignore

            do! semaphore.Acquire()
            do! Future.yield' ()
            do semaphore.Release()
        })
        workerTasks <- fTask :: workerTasks

    for wTask in workerTasks do
        let r = wTask.Await() |> Future.runBlocking
        Assert.Equal(r, Ok ())

    Assert.True(semaphore.TryAcquire(5))
    Assert.False(semaphore.TryAcquire(1))
