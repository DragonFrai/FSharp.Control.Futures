open System
open System.Diagnostics
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Future.Base

//open FSharp.Control.Tasks.V2

open FSharp.Control.Future
open FSharp.Control.Future.Test


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


    let snowballFuture n = future {
        let rec loop x i : Future<Tree<int>> = future {
            if i >= n then
                return Leaf
            else
            let! left = loop (x + i) (i + 1)
            and! right = loop (x + i + 1) (i + 1)
            return Node (left, x ,right)
        }
        return! loop 0 0
    }

    let snowballFutureParallel n = future {
        let rec loop x i : Future<Tree<int>> = future {
            if i >= n then
                return Leaf
            else
            let left = loop (x + i) (i + 1) |> Runtime.runOnPoolAsync
            let right = loop (x + i + 1) (i + 1) |> Runtime.runOnPoolAsync
            let! left = left
            let! right = right
            return Node (left, x ,right)
        }
        return! loop 0 0
    }

    let snowballAsyncParallel n = async {
        let rec loop x i : Async<Tree<int>> = async {
            if i >= n then
                return Leaf
            else
            let left = loop (x + i) (i + 1)
            let right = loop (x + i + 1) (i + 1)
            let! [|left; right|] = Async.Parallel [left; right]
            return Node (left, x ,right)
        }
        return! loop 0 0
    }

    let snowballAsync n = async {
        let rec loop x i : Async<Tree<int>> = async {
            if i >= n then
                return Leaf
            else
            let! left = loop (x + i) (i + 1)
            let! right = loop (x + i + 1) (i + 1)
            return Node (left, x ,right)
        }
        return! loop 0 0
    }

//    let snowballTask n = task {
//        let rec loop x i : Task<Tree<int>> = task {
//            if i >= n then
//                return Leaf
//            else
//            let! left = loop (x + i) (i + 1)
//            let! right = loop (x + i + 1) (i + 1)
//            return Node (left, x ,right)
//        }
//        return! loop 0 0
//    }


let runFuture depth =
    let f = Snowball.snowballFuture depth
    Future.run f

let runFutureParallel depth =
    let f = Snowball.snowballFutureParallel depth
    Runtime.runSync f

let runAsync depth =
    let a = Snowball.snowballAsync depth
    Async.RunSynchronously a

let runAsyncParallel depth =
    let a = Snowball.snowballAsyncParallel depth
    Async.RunSynchronously a

//let runTask depth =
//    let task = Snowball.snowballTask depth
//    task.GetAwaiter().GetResult()

module Fib =

//    let inline private futureM1 n = Future.ready (n - 1)
//    let inline private futureM2 n = Future.ready (n - 2)

    let rec fibFunction () : int -> int =
        fun n ->
            if n < 0 then raise (InvalidOperationException "n < 0")
            if n < 2 then n
            else fibFunction () (n - 1) + fibFunction () (n - 2)

    let rec fibAsync (n: int) = async {
        if n < 2 then
            return n
        else
            let! a = fibAsync (n - 1)
            let! b = fibAsync (n - 2)
            return a + b
    }

    let rec fibFuture (n: int) = future {
        if n < 2 then
            return n
        else
            let! a = fibFuture (n - 1)
            let! b = fibFuture (n - 2)
            return a + b
    }

    let rec fibFutureNoBuilder (n: int) : Future<int> =
        if n < 0 then raise (InvalidOperationException "n < 0")
        if n < 2
        then Future.ready n
        else
            let mutable value = -1
            let f1 = fibFutureNoBuilder (n - 1)
            let f2 = fibFutureNoBuilder (n - 2)
            Future (fun waker ->
                match value with
                | -1 ->
                    match Future.poll waker f1, Future.poll waker f2 with
                    | Ready a, Ready b -> value <- (a + b); Ready (a + b)
                    | _ -> Pending
                | x -> Ready x
            )

module QuoteBuilders =
    open FSharp.Quotations

    type StateMachineFutureBuilder with
        member _.Quote() = ()
        member _.Run(x: Expr) = x

    let print x =
        QuotationPrinting.

[<EntryPoint>]
let main argv =

    let fut = smfuture {
        return 12
    }

    let r = fut |> Future.run

    printfn $"{r}"

//    printfn "Main"
//    future {
//        let! x1 = future {
//            printfn "[x1] Start"
//            do! Future.sleep 2000
//            printfn "[x1] End"
//            return 2
//        }
//        and! x2 = future {
//            printfn "[x2] Start"
//            do! Future.sleep 3000
//            printfn "[x2] End"
//            return 3
//        }
//        and! x3 = future {
//            printfn "[x3]"
//            return 1
//        }
//        printfn "Merged"
//        return x1 + x2 + x3
//    }
//    |> Runtime.runSync
//    |> printfn "R: %i"

//    let depth = 25
//
//    let sw = Stopwatch()
//
//    printfn "Test function..."
//    sw.Start()
//    for i in 1..20 do (Fib.fibFunction () depth) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms
//
//    printfn "Test async..."
//    sw.Restart()
//    for i in 1..20 do (Async.RunSynchronously(Fib.fibAsync depth)) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms
//
//    printfn "Test future..."
//    sw.Restart()
//    for i in 1..20 do (Fib.fibFuture depth |> Future.run) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms
//
//    printfn "Test futureNoBuilder..."
//    sw.Restart()
//    for i in 1..20 do (Fib.fibFutureNoBuilder depth |> Future.run) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms

    0 // return an integer exit code
