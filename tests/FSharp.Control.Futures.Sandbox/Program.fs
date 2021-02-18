module FSharp.Control.Futures.Sandbox.Program

open System
open System.Diagnostics

//open FSharp.Control.Tasks.V2

open FSharp.Control.Futures.Base
open FSharp.Control.Futures
open FSharp.Control.Futures.FutureRt
open Hopac
open Hopac.Infixes

let inline ( ^ ) f x = f x


module Fib =

    let rec fib n =
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then  n
        else fib(n-1) + fib(n-2)

    let rec fibAsync n =
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then async { return n }
        else async {
            let! a = fibAsync (n - 1)
            let! b = fibAsync (n - 2)
            return a + b
        }

    open FSharp.Control.Tasks
    let rec fibTask n =
        if n < 0 then invalidOp "n < 0"
        task {
            if n <= 1 then return n
            else
                let! a = fibTask (n - 1)
                let! b = fibTask (n - 2)
                return a + b
        }

    let rec fibJob n = job {
        if n < 2 then
            return n
        else
            let! x = fibJob (n-2)
            let! y = fibJob (n-1)
            return x + y
    }

    let rec fibJobParallel n = job {
            if n < 2 then
                return n
            else
                let! (x, y) = fibJob ^ n-2 <*> fibJob ^ n-1
                return x + y
        }

    let rec fibFuture (n: int) : Future<int> =
        if n < 0 then invalidOp "n < 0"
        future {
            if n <= 1 then return n
            else
                let! a = fibFuture (n - 1)
                let! b = fibFuture (n - 2)
                return a + b
        }

    let rec fibFutureAsyncOnRuntime (n: int) : Future<int> =
        if n < 0 then invalidOp "n < 0"
        future {
            if n <= 1 then return n
            else
                let! a = FutureRt.runAsync (fibFuture (n - 1))
                and! b = FutureRt.runAsync (fibFuture (n - 2))
                return a + b
        }

    let rec fibFutureCombinators (n: int) : Future<int> =
        if n < 0 then invalidOp "n < 0"

        Future.lazy' ^fun () ->
            if n <= 1
            then Future.ready n
            else
                Future.merge (fibFutureCombinators (n-1)) (fibFutureCombinators (n-2))
                |> Future.map ^fun (x, y) -> x + y
        |> Future.join

    let fibFutureOptimized n =
        if n < 0 then invalidOp "n < 0"
        let rec fibInner n =
            if n <= 1 then Future.ready n
            else
                let mutable value = -1
                let f1 = fibInner (n-1)
                let f2 = fibInner (n-2)
                Future.Core.create ^fun waker ->
                    match value with
                    | -1 ->
                        match Future.Core.poll waker f1, Future.Core.poll waker f2 with
                        | Ready a, Ready b ->
                            value <- a + b
                            Ready (a + b)
                        | _ -> Pending
                    | value -> Ready value

        let fut = lazy(fibInner n)
        Future.Core.create ^fun w -> Future.Core.poll w fut.Value

    let runPrimeTest () =
        let sw = Stopwatch()
        let n = 27

        printfn "Test function..."
        sw.Start()
        for i in 1..20 do fib n |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Job..."
        sw.Start()
        for i in 1..20 do (fibJob n |> Hopac.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Tasks..."
        sw.Start()
        for i in 1..20 do (fibTask n).GetAwaiter().GetResult() |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

//        printfn "Test Async..."
//        sw.Start()
//        for i in 1..20 do (fibAsync n |> Async.RunSynchronously) |> ignore
//        let ms = sw.ElapsedMilliseconds
//        printfn "Total %i ms\n" ms
//
        printfn "Test Future..."
        sw.Restart()
        for i in 1..20 do (fibFutureOptimized n |> Future.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms
//
        printfn "Test Future Combinators..."
        sw.Restart()
        for i in 1..20 do (fibFutureCombinators n |> Future.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms



[<EntryPoint>]
let main argv =
    Fib.runPrimeTest ()



    0 // return an integer exit code
