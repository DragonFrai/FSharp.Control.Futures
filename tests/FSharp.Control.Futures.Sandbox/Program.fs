module FSharp.Control.Futures.Sandbox.Program

open System
open System.Diagnostics
open System.Threading.Tasks
open System.Threading

//open FSharp.Control.Tasks.V2

open FSharp.Control.Futures.Base
open FSharp.Control.Futures


let inline ( ^ ) f x = f x

module Snowball =

    type Tree<'a> =
        | Leaf
        | Node of Tree<'a> * 'a * Tree<'a>

    let snowballFunction n =
        let rec loop x i : Tree<int> =
            if i >= n then
                Leaf
            else
            let left = loop (x + i) (i + 1)
            let right = loop (x + i + 1) (i + 1)
            Node (left, x ,right)
        loop 0 0

    let snowballAsync n =
        let rec loop x i : Async<Tree<int>> =
            if i >= n then async { return Leaf }
            else async {
                let! left = loop (x + i) (i + 1)
                let! right = loop (x + i + 1) (i + 1)
                return Node (left, x ,right)
            }
        async { return! loop 0 0 }

    let snowballFuture n =
        let rec loop x i : Future<Tree<int>> =
            if i >= n then future { return Leaf }
            else future {
                let! left = loop (x + i) (i + 1)
                let! right = loop (x + i + 1) (i + 1)
                return Node (left, x ,right)
            }

        future { return! loop 0 0 }


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

    let rec fibFuture (n: int) : Future<int> =
        if n < 0 then invalidOp "n < 0"
        future {
            if n <= 1 then return n
            else
                let! a = fibFuture (n - 1)
                let! b = fibFuture (n - 2)
                return a + b
        }

    let fibFutureOptimized n =
        if n < 0 then invalidOp "n < 0"
        let rec fibInner n =
            if n <= 1 then Future.ready n
            else
                let mutable value = -1
                let f1 = fibInner (n-1)
                let f2 = fibInner (n-2)
                Future.create ^fun waker ->
                    match value with
                    | -1 ->
                        match Future.poll waker f1, Future.poll waker f2 with
                        | Ready a, Ready b ->
                            value <- a + b
                            Ready (a + b)
                        | _ -> Pending
                    | value -> Ready value

        let fut = lazy(fibInner n)
        Future.create ^fun w -> Future.poll w fut.Value

    let runPrimeTest () =
        let sw = Stopwatch()
        let n = 25

        printfn "Test function..."
        sw.Start()
        for i in 1..20 do fib n |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Tasks..."
        sw.Start()
        for i in 1..20 do (fibTask n).GetAwaiter().GetResult() |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Async..."
        sw.Start()
        for i in 1..20 do (fibAsync n |> Async.RunSynchronously) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Future..."
        sw.Restart()
        for i in 1..20 do (fibFuture n |> Future.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Future low level..."
        sw.Restart()
        for i in 1..20 do (fibFutureOptimized n |> Future.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

[<EntryPoint>]
let main argv =

//    let sw = Stopwatch()
//    let n = 20
//
//    printfn "Test function..."
//    sw.Start()
//    for i in 1..20 do (Snowball.snowballFunction n) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms
//
//    printfn "Test async..."
//    sw.Start()
//    for i in 1..20 do (Snowball.snowballAsync n |> Async.RunSynchronously) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms
//
//    printfn "Test future..."
//    sw.Start()
//    for i in 1..20 do (Snowball.snowballFuture n |> Future.run) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms
//
//    printfn "Test future !RAW SM!..."
//    sw.Start()
//    for i in 1..20 do (Snowball.snowballFutureRawSM n |> Future.run) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms

    Fib.runPrimeTest ()

    0 // return an integer exit code
