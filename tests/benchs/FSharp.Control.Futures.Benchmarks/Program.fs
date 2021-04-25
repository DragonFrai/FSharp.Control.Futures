module FSharp.Control.Futures.Benchmarks.Program

open System

open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running

open FSharp.Control.Futures

open FSharp.Control.Futures.Benchmarks.Fibonacci
open FSharp.Control.Futures.Scheduling


type FibonacciBenchmark() =

    member _.Arguments() =
        seq {
            yield! seq { 0 .. 1 .. 5 }
            yield! seq { 10 .. 5 .. 25 }
        }

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialFun(n) =
        SerialFun.fib n

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialFutureBuilder(n) =
        SerialFutureBuilder.fib n |> Future.runSync

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.MergeFutureBuilder(n) =
        MergeFutureBuilder.fibMerge n |> Future.runSync

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialAsync(n) =
        SerialAsync.fib n |> Async.RunSynchronously

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.ParallelAsync(n) =
        ParallelAsync.fib n |> Async.RunSynchronously

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialJobBuilder(n) =
        SerialJob.fib n |> Hopac.Hopac.run

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialTask(n) =
        (SerialTask.fib n).GetAwaiter().GetResult()

[<EntryPoint>]
let main argv =
    let summary = BenchmarkRunner.Run<FibonacciBenchmark>()
    0
