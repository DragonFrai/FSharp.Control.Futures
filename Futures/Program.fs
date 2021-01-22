open System
open System.Diagnostics
open System.Threading.Tasks
open Futures
open System.Threading
open FSharp.Control.Tasks.V2

//type FutureBuilder with
//    member _.Run(expr: Expr) = expr
//    member _.Quote() = ()
//    
//    

module Snowball =
    open Futures
    
    
    
    type Tree<'a> =
        | Leaf
        | Node of Tree<'a> * 'a * Tree<'a>
    
    let snowballFuture n = future {
        let rec loop x i : Future<Tree<int>> = future {
            if i >= n then
                return Leaf
            else
            let! left = loop (x + i) (i + 1)
            let! right = loop (x + i + 1) (i + 1)
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
    
    let snowballTask n = task {
        let rec loop x i : Task<Tree<int>> = task {
            if i >= n then
                return Leaf
            else 
            let! left = loop (x + i) (i + 1)
            let! right = loop (x + i + 1) (i + 1)
            return Node (left, x ,right)
        }
        return! loop 0 0
    }


let runFuture depth =
    let f = Snowball.snowballFuture depth
    Runtime.runSync f

let runFutureParallel depth =
    let f = Snowball.snowballFutureParallel depth
    Runtime.runSync f

let runAsync depth =
    let a = Snowball.snowballAsync depth
    Async.RunSynchronously a

let runAsyncParallel depth =
    let a = Snowball.snowballAsyncParallel depth
    Async.RunSynchronously a

let runTask depth =
    let task = Snowball.snowballTask depth
    task.GetAwaiter().GetResult()


[<EntryPoint>]
let main argv =
    
    
    0 // return an integer exit code
    