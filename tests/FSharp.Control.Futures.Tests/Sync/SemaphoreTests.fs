module FSharp.Control.Futures.Tests.SemaphoreTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync
open Xunit


[<Fact>]
let ``Semaphore without permits can be instanced``() =
    let _s = Semaphore(0)
    ()

[<Fact>]
let ``Try acquire``() =
    let s = Semaphore(1)
    do
        do Expect.isTrue (s.TryAcquire()) ""
        do Expect.isFalse (s.TryAcquire()) ""
        do s.Release()
    do Expect.isTrue (s.TryAcquire()) ""

[<Fact>]
let ``Acquire``() =
    Tests.repeat 100 (fun () ->
        let s = Semaphore(1)
        do Expect.isTrue (s.TryAcquire())
        let fTask = ThreadPoolRuntime.spawn (future {
            return! s.Acquire()
        })
        s.Release(1)
        Expect.equal (fTask.Await() |> Future.runBlocking) (Ok ()) ""
        ()
    )

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

    Expect.equal counter expectedResult "Counter not synchronized"

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

    Expect.isTrue (semaphore.TryAcquire(5)) ""
    Expect.isFalse (semaphore.TryAcquire(1)) ""
