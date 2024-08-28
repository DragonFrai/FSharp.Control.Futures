module FSharp.Control.Futures.Tests.Runtime.ThreadPoolRuntimeTests


open System.Threading
open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime


let threadPoolRuntimeWorks = test "ThreadPoolRuntime regular path" {

    let fut = future {
        let! a = Future.ready 1
        let! b = Future.yieldWorkflow () |> Future.bind (fun () -> Future.ready 2)
        let c = a + b
        return c
    }

    let fTask = ThreadPoolRuntime.spawn fut
    let result = fTask.Await() |> Future.runBlocking |> Result.get
    Expect.equal result 3 "Result not expected"
}

let threadPoolRuntimeAbortingWorks = test "ThreadPoolRuntime aborting" {
    use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
    let mutable isCompleted = false
    let fut = future {
        wh.WaitOne()
        do! Future.yieldWorkflow ()
        isCompleted <- true
    }

    let fTask = ThreadPoolRuntime.spawn fut
    fTask.Abort()
    wh.Set()

    let result = fTask.Await() |> Future.runBlocking

    Expect.equal result (Error AwaitError.Aborted) "Result not expected"
    Expect.equal isCompleted false "Aborted future completed"
}

[<Tests>]
let tests =
    testList "ThreadPoolRuntime" [
        threadPoolRuntimeWorks
        threadPoolRuntimeAbortingWorks
    ]
