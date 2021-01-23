module FSharp.Control.Future.Benchmarks.Program

open System
open BenchmarkDotNet.Attributes

open BenchmarkDotNet.Running
open FSharp.Control.Future
open FSharp.Control.Future.Benchmarks.Fibonacci


type FibonacciBenchmark() =

    member _.Arguments() = seq { 0 .. 10 .. 20 }

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialFun(n) =
        SerialFun.fib n


    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialFutureBuilder(n) =
        SerialFutureBuilder.fib n |> Runtime.runSync

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.ParallelFutureBuilder(n) =
        ParallelFutureBuilder.fib n |> Runtime.runSync


    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialAsync(n) =
        SerialAsync.fib n |> Async.RunSynchronously

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.ParallelAsync(n) =
        ParallelAsync.fib n |> Async.RunSynchronously


[<EntryPoint>]
let main argv =
    let summary = BenchmarkRunner.Run<FibonacciBenchmark>()
    0
