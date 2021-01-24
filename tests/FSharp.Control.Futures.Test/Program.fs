open System
open System.Diagnostics
open System.Threading.Tasks
open System.Threading

//open FSharp.Control.Tasks.V2

open FSharp.Control.Futures.Base
open FSharp.Control.Futures


//module Snowball =
//
//    type Tree<'a> =
//        | Leaf
//        | Node of Tree<'a> * 'a * Tree<'a>
//
//    let snowballFunction n =
//        let rec loop x i : Tree<int> =
//            if i >= n then
//                Leaf
//            else
//            let left = loop (x + i) (i + 1)
//            let right = loop (x + i + 1) (i + 1)
//            Node (left, x ,right)
//        loop 0 0
//
//
//    let snowballFuture n = future {
//        let rec loop x i : Future<Tree<int>> = future {
//            if i >= n then
//                return Leaf
//            else
//            let! left = loop (x + i) (i + 1)
//            and! right = loop (x + i + 1) (i + 1)
//            return Node (left, x ,right)
//        }
//        return! loop 0 0
//    }
//
//    let snowballFutureParallel n = future {
//        let rec loop x i : Future<Tree<int>> = future {
//            if i >= n then
//                return Leaf
//            else
//            let left = loop (x + i) (i + 1) |> Runtime.runOnPoolAsync
//            let right = loop (x + i + 1) (i + 1) |> Runtime.runOnPoolAsync
//            let! left = left
//            let! right = right
//            return Node (left, x ,right)
//        }
//        return! loop 0 0
//    }
//
//    let snowballAsyncParallel n = async {
//        let rec loop x i : Async<Tree<int>> = async {
//            if i >= n then
//                return Leaf
//            else
//            let left = loop (x + i) (i + 1)
//            let right = loop (x + i + 1) (i + 1)
//            let! [|left; right|] = Async.Parallel [left; right]
//            return Node (left, x ,right)
//        }
//        return! loop 0 0
//    }
//
//    let snowballAsync n = async {
//        let rec loop x i : Async<Tree<int>> = async {
//            if i >= n then
//                return Leaf
//            else
//            let! left = loop (x + i) (i + 1)
//            let! right = loop (x + i + 1) (i + 1)
//            return Node (left, x ,right)
//        }
//        return! loop 0 0
//    }
//
////    let snowballTask n = task {
////        let rec loop x i : Task<Tree<int>> = task {
////            if i >= n then
////                return Leaf
////            else
////            let! left = loop (x + i) (i + 1)
////            let! right = loop (x + i + 1) (i + 1)
////            return Node (left, x ,right)
////        }
////        return! loop 0 0
////    }

open FSharp.Control.Futures.Test
open FSharp.Quotations

//module QuoteExpr =
//    type FutureBuilder with
//        member _.Quote() = ()
//        member _.Run(x: Expr) = x
//
//open QuoteExpr

module Fib =

    let rec fib n =
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then n
        else fib (n - 1) + fib (n - 2)

    let rec fibAsync n =
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then async { return n }
        else async {
            let! a = fibAsync (n - 1)
            let! b = fibAsync (n - 2)
            return a + b
        }


    let rec fibFutureNoBuilder (n: int) : IFuture<int> =
        if n < 0 then raise (InvalidOperationException "n < 0")
        if n < 2
        then Future.ready n
        else
            let mutable value = -1
            let f1 = fibFutureNoBuilder (n - 1)
            let f2 = fibFutureNoBuilder (n - 2)
            Future.create (fun waker ->
                match value with
                | -1 ->
                    match Future.poll waker f1, Future.poll waker f2 with
                    | Ready a, Ready b -> value <- (a + b); Ready (a + b)
                    | _ -> Pending
                | x -> Ready x
            )

    let rec fibFuture n = legacyfuture {
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then return n
        else
            let! a = fibFuture (n - 1)
            let! b = fibFuture (n - 2)
            return a + b
    }

    open FSharp.Control.Tasks
    let rec fibTask n = task {
        if n < 0 then invalidOp "n < 0"
        if n <= 1 then return n
        else
            let! a = fibTask (n - 1)
            let! b = fibTask (n - 2)
            return a + b
    }


    let rec fibSMFuture n =
        future {
            if n < 0 then invalidOp "n < 0"
            return!
                if n <= 1 then  future { return n }
                else
                    future {
                        let! a = fibSMFuture (n - 1)
                        let! b = fibSMFuture (n - 2)
                        return a + b
                    }
        }

//let runTask depth =
//    let task = Snowball.snowballTask depth
//    task.GetAwaiter().GetResult()

let inline (|HasRemoveDelay|) (x: ^a when ^a: (static member RemoveDelay: 'fa1 -> 'fa2) and 'fa1 :> IFuture<'a> and 'fa2 :> IFuture<'a>) = x

[<EntryPoint>]
let main argv =

    let sw = Stopwatch()
    let n = 25

    printfn "Test function..."
    sw.Start()
    for i in 1..20 do (Fib.fib n) |> ignore
    let ms = sw.ElapsedMilliseconds
    printfn "Total %i ms\n" ms

    printfn "Test tasks..."
    sw.Start()
    for i in 1..20 do (Fib.fibTask n).GetAwaiter().GetResult() |> ignore
    let ms = sw.ElapsedMilliseconds
    printfn "Total %i ms\n" ms

    printfn "Test async..."
    sw.Start()
    for i in 1..20 do (Fib.fibAsync n |> Async.RunSynchronously) |> ignore
    let ms = sw.ElapsedMilliseconds
    printfn "Total %i ms\n" ms

    printfn "Test future no builder..."
    sw.Restart()
    for i in 1..20 do (Fib.fibFutureNoBuilder n |> Future.run) |> ignore
    let ms = sw.ElapsedMilliseconds
    printfn "Total %i ms\n" ms

    printfn "Test State Machine Future..."
    sw.Restart()
    for i in 1..20 do (Fib.fibSMFuture n |> Future.run) |> ignore
    let ms = sw.ElapsedMilliseconds
    printfn "Total %i ms\n" ms

//    printfn "Test legacy future..."
//    sw.Restart()
//    for i in 1..20 do (Fib.fibFuture n |> Future.run) |> ignore
//    let ms = sw.ElapsedMilliseconds
//    printfn "Total %i ms\n" ms

    0 // return an integer exit code
