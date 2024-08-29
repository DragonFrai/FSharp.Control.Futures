module FSharp.Control.Futures.Tests.SemaphoreTests

open System
open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync


let semaphoreWorks = test "semaphoreWorks" {
    let workers = 100
    let perWorkerIterations = 10000
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
}

[<Tests>]
let tests =
    testList "Semaphore" [
        semaphoreWorks
    ]
