module FSharp.Control.Future.Benchmarks.Fibonacci

open FSharp.Control.Future


module SerialFun =
    let rec fib n =
        if n < 2L then
            n
        else
            fib (n - 1L) + fib (n - 2L)

module SerialFutureBuilder =
    let rec fib n = future {
        if n < 2L then
            return n
        else
            let! x = fib (n - 1L)
            let! y = fib (n - 2L)
            return x + y
    }

module ParallelFutureBuilder =
    let rec fib n = future {
        if n < 2L then
            return n
        else
            let! x = fib (n - 1L)
            and! y = fib (n - 2L)
            return x + y
    }

module SerialAsync =
    let rec fib n = async {
        if n < 2L then
            return n
        else
            let! x = fib (n - 1L)
            let! y = fib (n - 2L)
            return x + y
    }

module ParallelAsync =

    type FSharp.Control.AsyncBuilder with
        member _.MergeSources(x1, x2) =
            async {
                let x1 = async {
                    let! r = x1
                    return Choice1Of2 r
                }
                let x2 = async {
                    let! r = x2
                    return Choice2Of2 r
                }
                let! rs = Async.Parallel([x1; x2])
                let r1 = rs |> Array.pick (function Choice1Of2 r -> Some r | _ -> None)
                let r2 = rs |> Array.pick (function Choice2Of2 r -> Some r | _ -> None)
                return r1, r2
            }

    let rec fib n = async {
        if n < 2L then
            return n
        else
            let! x = fib (n - 1L)
            and! y = fib (n - 2L)
            return x + y
    }

