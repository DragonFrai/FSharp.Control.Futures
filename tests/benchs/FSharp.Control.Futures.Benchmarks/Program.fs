module FSharp.Control.Futures.Benchmarks.Program

open System

open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running

open FSharp.Control.Futures

open FSharp.Control.Futures.Benchmarks.Fibonacci
open FSharp.Control.Futures.Scheduling


[<HtmlExporter>]
[<CsvExporter>]
[<CsvMeasurementsExporter>]
[<PlainExporter>]
[<RPlotExporter>]
[<MarkdownExporterAttribute.GitHub>]
type FibonacciBenchmark() =

    member _.Arguments() =
        seq {
            yield 0
            yield! seq { 5 .. 5 .. 15 }
        }

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialFun(n) =
        SerialFun.fib n

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialFuture(n) =
        SerialFutureBuilder.fib n |> Future_OLD.runSync

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.MergeFuture(n) =
        MergeFutureBuilder.fibMerge n |> Future_OLD.runSync

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialAsync(n) =
        SerialAsync.fib n |> Async.RunSynchronously

//    [<Benchmark>]
//    [<ArgumentsSource("Arguments")>]
//    member _.ParallelAsync(n) =
//        ParallelAsync.fib n |> Async.RunSynchronously

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    [<GcServer>]
    member _.SerialJob(n) =
        SerialJob.fib n |> Hopac.Hopac.run

    [<Benchmark>]
    [<ArgumentsSource("Arguments")>]
    member _.SerialTask(n) =
        (SerialTask.fib n).GetAwaiter().GetResult()

[<EntryPoint>]
let main argv =
    let summary = BenchmarkRunner.Run<FibonacciBenchmark>()
    0
