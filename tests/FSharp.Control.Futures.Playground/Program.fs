module FSharp.Control.Futures.Sandbox.Program

open System
open System
open System.Diagnostics

//open FSharp.Control.Tasks.V2

open System.Text
open FSharp.Control.Futures
open FSharp.Control.Futures.Playground
open FSharp.Control.Futures.Scheduling
open FSharp.Control.Futures.Streams
open FSharp.Control.Futures.Streams.Channels
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

    let rec fibFuture (n: int) : Future<int> =
        if n < 0 then invalidOp "n < 0"
        future {
            if n <= 1 then return n
            else
                let! a = fibFuture (n - 1)
                let! b = fibFuture (n - 2)
                return a + b
        }

    let rec fibJobParallel n =
        if n < 0 then invalidOp "n < 0"
        job {
            if n < 2 then
                return n
            else
                let! (x, y) = fibJobParallel ^ n-2 <*> fibJobParallel ^ n-1
                return x + y
        }

    let rec fibFutureParallel (n: int) (sh: IScheduler) : Future<int> =
        if n < 0 then invalidOp "n < 0"
        future {
            if n <= 1 then return n
            else
                let a = fibFutureParallel (n - 1) (sh) |> Scheduler.spawnOn sh
                let! b = fibFutureParallel (n - 2) (sh)
                and! a = a
                return a + b
        }

    let fibFutureRaw n =
        if n < 0 then invalidOp "n < 0"
        let rec fibInner n =
            if n <= 1 then Future.ready n
            else
                let mutable value = -1
                let f1 = fibInner (n-1)
                let f2 = fibInner (n-2)
                Future.Core.create
                <| fun waker ->
                    match value with
                    | -1 ->
                        match Future.Core.poll waker f1, Future.Core.poll waker f2 with
                        | Poll.Ready a, Poll.Ready b ->
                            value <- a + b
                            Poll.Ready (a + b)
                        | _ -> Poll.Pending
                    | value -> Poll.Ready value
                <| fun () -> do ()

        let fut = lazy(fibInner n)
        Future.Core.create ^fun w -> Future.Core.poll w fut.Value

    let runPrimeTest () =
        let sw = Stopwatch()
        let n = 30

        let scheduler = Schedulers.threadPool

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

        // Job/Future no parallel
        printfn "Test Future..."
        sw.Restart()
        for i in 1..20 do (fibFuture n |> Future.runSync) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Job..."
        sw.Start()
        for i in 1..20 do (fibJob n |> Hopac.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        // Job/Future parallel

        printfn "Test Job parallel..."
        sw.Start()
        for i in 1..20 do (fibJobParallel n |> Hopac.run) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms

        printfn "Test Future parallel..."
        sw.Restart()
        for i in 1..20 do (fibFutureParallel n scheduler |> Future.runSync) |> ignore
        let ms = sw.ElapsedMilliseconds
        printfn "Total %i ms\n" ms



let main1 () =
    //    Fib.runPrimeTest ()

    let source = Stream.ofSeq [1; 2; 3]

    let fut2 = future {
        let ch = Bridge.create ()
        let! _ = future {
            for x in ch do
                printfn "%i" x
        }
        and! _ = future {
            for i in (Seq.init 100 id)  do
                Sender.send i ch
                do! Future.sleepMs 100
            do ch.Dispose()
        }
        ()
    }

    printfn "run future"
    fut2 |> Future.runSync

    System.Threading.Thread.Sleep(500)
    ()



let main2 () =

    let xs =
        stream {
            yield 0
            yield! [ 1; 2 ]
            do! Future.sleepMs 5000
            yield 3
            let! x = Future.ready 4
            yield x
        }

    xs
    |> Stream.iter (printfn "%A")
    |> Future.runSync

    ()


let main0 () =

    let myScheduler = Schedulers.threadPool

    let fut = future {
        let ch = Bridge.create ()

        let! () =
            future {
                for x in ch do
                    printfn "%i" x
            } |> Scheduler.spawnOn myScheduler

        and! () = future {
            let! () =
                future {
                    for i in 100..199  do
                        Sender.send i ch
                        do! Future.sleepMs 100
                } |> Scheduler.spawnOn myScheduler
            and! () =
                future {
                    for i in 200..299 do
                        Sender.send i ch
                        do! Future.sleepMs 100
                } |> Scheduler.spawnOn myScheduler
            do ch.Dispose()
        }

        ()
    }

    let () = Future.runSync fut
    ()


module File =

    open System.IO

    let readAllText (path: string) : Future<string> =
        future {
            let task = File.ReadAllTextAsync(path)
            return! Future.ofTask task
        }

    let readStream (bufferSize: int) (path: string) : IStream<byte> =
        stream {
            let fileStream = File.OpenRead(path)
            let count = bufferSize
            let buffer = Array.zeroCreate count
            let rec loop () = stream {
                let! c = fileStream.ReadAsync(buffer, 0, count) |> Future.ofTask
                if c > 0 then
                    yield! Stream.ofSeq buffer.[0..c-1]
                    yield! loop ()
                else
                    ()
            }
            yield! loop ()
        }

let getRandomBytes () = stream {
    let bufferSize = 32
    let bytes = File.readStream bufferSize "/dev/urandom"
    yield! bytes
}

let getRandomInts () = stream {
        let ints =
            getRandomBytes ()
            |> Stream.bufferByCount sizeof<int>
            |> Stream.map ^fun bytes -> BitConverter.ToInt32(ReadOnlySpan(bytes))
        yield! ints
    }

[<EntryPoint>]
let main argv =

    let scanner = SimpleRGrep.scanAllRec "/home/vlad/" "2018" Schedulers.threadPool 2
    Future.runSync scanner

    0 // return an integer exit code
