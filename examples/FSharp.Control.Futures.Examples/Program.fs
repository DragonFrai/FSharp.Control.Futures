module FSharp.Control.Futures.Sandbox.Program

open System
open System.Diagnostics
open System.Text
open System.Threading.Tasks

open FSharp.Control.Futures.Playground
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync
open FSharp.Control.Tasks
open Hopac
open Hopac.Infixes

open FSharp.Control.Futures


let inline ( ^ ) f x = f x


module Fib =

//    let rec fib n =
//        if n < 0 then invalidOp "n < 0"
//        if n <= 1 then  n
//        else fib(n-1) + fib(n-2)
//
    let rec fibAsync n =
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then async { return n }
        else async {
            let! a = fibAsync (n - 1)
            let! b = fibAsync (n - 2)
            return a + b
        }
//
//    open FSharp.Control.Tasks
//    let rec fibTask n =
//        if n < 0 then invalidOp "n < 0"
//        task {
//            if n <= 1 then return n
//            else
//                let! a = fibTask (n - 1)
//                let! b = fibTask (n - 2)
//                return a + b
//        }
//
    let rec fibJob n = job {
        if n < 2 then
            return n
        else
            let! x = fibJob (n-2)
            let! y = fibJob (n-1)
            return x + y
    }

    let rec fibFuture (n: int) : Future<int> =
        if n < 0 then invalidOp "n < 0"
        future {
            if n <= 1 then return n
            else
                do! Future.yieldWorkflow ()
                let! a = fibFuture (n - 1)
                do! Future.yieldWorkflow ()
                let! b = fibFuture (n - 2)
                do! Future.yieldWorkflow ()
                return a + b
        }

//    let rec fibJobParallel n =
//        if n < 0 then invalidOp "n < 0"
//        job {
//            if n < 2 then
//                return n
//            else
//                let! (x, y) = fibJobParallel ^ n-2 <*> fibJobParallel ^ n-1
//                return x + y
//        }
//
//    let rec fibFutureParallel (n: int) (sh: IScheduler) : IComputation<int> =
//        if n < 0 then invalidOp "n < 0"
//        future {
//            if n <= 1 then return n
//            else
//                let a = fibFutureParallel (n - 1) (sh) |> Scheduler.spawnOn sh
//                let! b = fibFutureParallel (n - 2) (sh)
//                and! a = a
//                return a + b
//        }
//
//    let fibFutureRaw n =
//        if n < 0 then invalidOp "n < 0"
//        let rec fibInner n =
//            if n <= 1 then Future.ready n
//            else
//                let mutable value = -1
//                let f1 = fibInner (n-1)
//                let f2 = fibInner (n-2)
//                Future.Core.create
//                <| fun waker ->
//                    match value with
//                    | -1 ->
//                        match Future.Core.poll waker f1, Future.Core.poll waker f2 with
//                        | Poll.Ready a, Poll.Ready b ->
//                            value <- a + b
//                            Poll.Ready (a + b)
//                        | _ -> Poll.Pending
//                    | value -> Poll.Ready value
//                <| fun () -> do ()
//
//        let fut = lazy(fibInner n)
//        Future.Core.create ^fun w -> Future.Core.poll w fut.Value

    let deepFutureComputation n =
        let rec loop n =
            match n with
            | 0 -> future { return n }
            | n ->
                future {
                    let! f1 = Future.ready 2
                    do! Future.yieldWorkflow ()
                    let! f2 = loop (n - 1)
                    do! Future.yieldWorkflow ()
                    let! f3 = Future.ready 3
                    return f1 + f2 + f3
                }
        loop n

    let testDeep n =
        printfn $"Test with n = {n}"
        let sw = Stopwatch()

        printfn "Test deep computation..."
        sw.Restart()
        for i in 1..20 do deepFutureComputation n |> Future.runBlocking |> ignore
        printfn $"Total {sw.ElapsedMilliseconds} ms"


    let runPrimeTest n =
        let sw = Stopwatch()

        let scheduler = ThreadPoolRuntime.Instance

        printfn "Test with n = %d" n

        // printf "Test Future...      "
        // sw.Restart()
        // for i in 1..20 do fibFuture n |> Future.runSync |> ignore
        // let ms = sw.ElapsedMilliseconds
        // printfn "Total %i ms" ms

        printf "Test Computation... "
        sw.Restart()
        for i in 1..20 do fibFuture n |> Future.runBlocking |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms" ms

        // // Async
        // printf "Test Async...       "
        // sw.Restart()
        // for i in 1..20 do (fibAsync n |> Async.RunSynchronously) |> ignore
        // let ms = sw.ElapsedMilliseconds
        // printfn "Total %i ms" ms
        //
        // // Job
        // printf "Test Job...         "
        // sw.Restart()
        // for i in 1..20 do (fibJob n |> Hopac.run) |> ignore
        // let ms = sw.ElapsedMilliseconds
        // printfn "Total %i ms" ms

        printfn ""
//
//        printfn "Test Job..."
//        sw.Start()
//        for i in 1..20 do (fibJob n |> Hopac.run) |> ignore
//        let ms = sw.ElapsedMilliseconds
//        printfn "Total %i ms\n" ms

        // Job/Future parallel
//
//        printfn "Test Job parallel..."
//        sw.Start()
//        for i in 1..20 do (fibJobParallel n |> Hopac.run) |> ignore
//        let ms = sw.ElapsedMilliseconds
//        printfn "Total %i ms\n" ms
//
//        printfn "Test Future parallel..."
//        sw.Restart()
//        for i in 1..20 do (fibFutureParallel n scheduler |> Future.runSync) |> ignore
//        let ms = sw.ElapsedMilliseconds
//        printfn "Total %i ms\n" ms



open FSharp.Control.Futures.Transforms.FutureTaskTransforms

open System.Threading

let testTasks () =
    let fut1 = Future.ready "a"
    // let task1 =
    // task {
    //     0
    // }
    let sw = Stopwatch()
    sw.Start()
    let t =
        ThreadPool.SetMaxThreads(10, 10) |> ignore
        task {
            printfn $"[{sw.Elapsed}]"
            let! r = Future.ready 1 |> Future.toTask
            printfn $"[{sw.Elapsed}] r: {r}"
            let! _ = Task.WhenAll([
                for i in 0 .. 10_000 - 1 ->
                    Future.sleepMs 10_000 |> Future.toTask
            ])
            printfn $"[{sw.Elapsed}]"
        }
    t.GetAwaiter().GetResult()

    ()

module Result =
    let get (r: Result<'a, 'e>) : 'a =
        match r with
        | Ok r -> r
        | Error e -> failwith $"{e}"

[<EntryPoint>]
let main argv =

    let ch = OneShot.Create()

    let receiver = future {
        let! msg = ch.Await()
        printfn $"Hello, {msg}"
        ()
    }

    let sender = future {
        do! Future.sleepMs 1000
        do ch.Send("World") |> ignore
    }

    let fTaskRx, fTaskTx =
        ThreadPoolRuntime.Instance.Spawn(receiver), ThreadPoolRuntime.Instance.Spawn(sender)

    fTaskRx.Await() |> Future.runBlocking |> Result.get
    fTaskTx.Await() |> Future.runBlocking |> Result.get

    let f = future {
        do! SimpleRGrep.scanAllRec "/home/dragon/Projects/" "sleep" ThreadPoolRuntime.Instance 256
    }
    f |> Future.runBlocking


    // RuntimeExamples.simpleExample ()

    // let adds (m: Sync.Mutex<int>) = future {
    //     printfn "TASK"
    //     let rec loop (m: Sync.Mutex<int>) (n: int) = future {
    //         if n >= 10_000 then ()
    //         else
    //             do! Sync.Mutex.updateSync (fun x -> x + 1) m
    //             do! Future.sleep (TimeSpan.FromMicroseconds(1.0))
    //             return! loop m (n+1)
    //     }
    //     return! loop m 0
    // }
    //
    // let mutex = Sync.Mutex(0)
    //
    // printfn "spawning futures"
    // let futures = Seq.init 100 (fun i -> (adds mutex))
    // let root = Seq.fold (fun cf f -> Future.merge cf f |> Future.ignore) (Future.unit') futures
    // printfn "join futures"
    //
    // let _ = Future.runSync root
    // printfn "printing result"
    // let guard = mutex.BlockingLock()
    // let v = guard.Value
    //
    // printfn $"value: {v}"

    // let r =
    //     future {
    //         let! () = Future.ready ()
    //         while true do
    //             printfn "a"
    //             let! x = Future.ready 0
    //             printf $"{x}"
    //         let! () = Future.ready ()
    //         return 0
    //     }

    // let f =
    //     future {
    //         let! a = future {
    //             let! x = Future.ready 1
    //             do! Future.sleepMs 100
    //             return x + 2
    //         }
    //         let! b = Future.ready true
    //         do! Future.sleepMs 100
    //         let! c = future {
    //             do! Future.sleepMs 100
    //             let! y = Future.ready "b"
    //             let! z = future {
    //                 let! c = Future.ready "c"
    //                 do! Future.sleepMs 100
    //                 return c + "c"
    //             }
    //             return y + z
    //         }
    //         do! Future.sleepMs 100
    //         return string a + string b + c
    //     }
    //
    // let r = Future.runSync f
    // printfn $"r: {r}"

    // testTasks ()

    // let n = 2000
    // Fib.testDeep n
    // Fib.testDeep n

    // computation {
    //     while true do
    //         do! AsyncComputation.yieldWorkflow ()
    // }
    // |> AsyncComputation.runSync

    // let n = 22
    // Fib.runPrimeTest n
    // Fib.runPrimeTest n

    // let fut =
    //     computation {
    //         while true do
    //             do! AsyncComputation.yieldWorkflow ()
    //     }
    //
    // (task {
    //     do! Task.Delay(20 * 1000)
    //     do fut.Cancel()
    // })
    //
    // let r = AsyncComputation.runSync fut
    // printfn $"r: {r}"


    // Fib.runPrimeTest n
    // Fib.runPrimeTest n
//
//    Fib.runPrimeTest 29
//    Fib.runPrimeTest 29
//
//    Fib.runPrimeTest 28
//    Fib.runPrimeTest 28

//    Fib.runPrimeTest 27
//    Fib.runPrimeTest 27
//
//    Fib.runPrimeTest 26
//    Fib.runPrimeTest 26
//
//    Fib.runPrimeTest 25
//    Fib.runPrimeTest 25
//
//    Fib.runPrimeTest 20
//    Fib.runPrimeTest 20

    0 // return an integer exit code
