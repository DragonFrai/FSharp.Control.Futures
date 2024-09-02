module FSharp.Control.Futures.Tests.Sync.SemaphoreTests

open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync
open Xunit


// TODO: Add drop future test as in mutex

[<Fact>]
let ``Semaphore creating with 0 permits``() =
    let _s = Semaphore(0)
    ()

[<Fact>]
let ``Try acquire``() =
    let s = Semaphore(1)
    do
        do Assert.True(s.TryAcquire())
        do Assert.False(s.TryAcquire())
        do s.Release()
    do Assert.True(s.TryAcquire())

[<Theory>]
[<Repeat(100)>]
let ``Acquire``() =
    let s = Semaphore(1)
    do Assert.True(s.TryAcquire())
    let fTask = ThreadPoolRuntime.spawn (future {
        return! s.Acquire()
    })
    s.Release(1)
    Assert.Equal(Ok (), fTask.Await() |> Future.runBlocking)
    ()

[<Fact>]
let ``Semaphore max permits``() =
    let _s = Semaphore(Semaphore.MaxPermits)
    let s' = Semaphore(Semaphore.MaxPermits - 1)
    s'.Release(1)

[<Fact>]
let ``Semaphore max permits overflow``() =
    let _ex = Assert.ThrowsAny(fun () ->
        let _s = Semaphore(Semaphore.MaxPermits + 1)
        ()
    )
    ()

[<Fact>]
let ``Semaphore add max permits``() =
    let s = Semaphore(0)
    s.Release(Semaphore.MaxPermits)
    Assert.Equal(Semaphore.MaxPermits, s.AvailablePermits)

[<Fact>]
let ``Semaphore add permits overflow 1``() =
    let s = Semaphore(1)
    let _ex = Assert.ThrowsAny(fun () -> s.Release(Semaphore.MaxPermits))
    ()

[<Fact>]
let ``Semaphore add permits overflow 2``() =
    let s = Semaphore(Semaphore.MaxPermits - 1)
    do s.Release(1)
    let _ex = Assert.ThrowsAny(fun () -> s.Release(1))
    ()

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
                do! Future.yieldWorkflow ()
        })
        workerTasks <- fTask :: workerTasks

    do semaphore.Release(1)
    for wTask in workerTasks do
        wTask.Await() |> Future.runBlocking

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
            do! Future.yieldWorkflow ()
            do semaphore.Release()
        })
        workerTasks <- fTask :: workerTasks

    for wTask in workerTasks do
        wTask.Await() |> Future.runBlocking

    Assert.True(semaphore.TryAcquire(5))
    Assert.False(semaphore.TryAcquire(1))
