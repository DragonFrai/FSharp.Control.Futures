namespace FSharp.Control.Futures.Scheduling

open System
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


type IJoinHandle<'a> =
    inherit Future<'a>
    // TODO: Add TryJoin and TryAwait
    abstract member Join: unit -> Result<'a, exn>

// Run future execution
type IScheduler =
    inherit IDisposable
    abstract Spawn: fut: Future<'a> -> IJoinHandle<'a>


// -------------------
// ThreadPollScheduler
// -------------------
module private rec ThreadPoolImpl =

    module private Utils =
        let inline queueTask (task: ThreadPoolTask<'a>) =
            ThreadPool.QueueUserWorkItem(fun _ -> do task.Run()) |> ignore

    type ThreadPoolTask<'a>(future: Future<'a>) as this =

        let mutable isComplete = false
        // 0 -- false ; 1 -- true
        let mutable isRequireWake = false // init with require to update
        // 0 -- false ; 1 -- true
        let mutable isInQueue = false

        let waiter: IVar<Result<'a, exn>> = IVar.create ()

        let sync = obj()

        let context =
            { new Context() with
                member _.Wake() =
                    lock sync <| fun () ->
                        isRequireWake <- true
                        if not isInQueue then
                            Utils.queueTask this
                            isInQueue <- true
            }

        member this.Run() =
            let isComplete' =
                lock sync ^fun () ->
                    isRequireWake <- false
                    isComplete

            if isComplete' then ()
            else
            try
                let x = Future.Core.poll context future
                match x with
                | Poll.Ready x ->
                    waiter.Put(Ok x)
                    lock sync ^fun () ->
                        isComplete <- true
                | Poll.Pending -> ()
            with e ->
                waiter.Put(Error e)

            lock sync ^fun () ->
                if isRequireWake && not isComplete
                then Utils.queueTask this
                else isInQueue <- false

        member _.InitForQueue() =
            isInQueue <- true
            isRequireWake <- true

        interface IJoinHandle<'a> with

            member _.Join() =
                Future.runSync waiter

            member _.Poll(context) =
                let x = Future.Core.poll context waiter
                match x with
                | Poll.Ready x ->
                    match x with
                    | Ok x -> Poll.Ready x
                    | Error e -> raise e
                | Poll.Pending -> Poll.Pending

            member _.Cancel() =
                lock sync <| fun () ->
                    waiter.Put(Error FutureCancelledException)
                    isComplete <- true
                future.Cancel()


    type ThreadPoolScheduler() =
        interface IScheduler with
            member this.Spawn(fut: Future<'a>) =
                let task = ThreadPoolTask<'a>(fut)
                task.InitForQueue()
                Utils.queueTask task
                task :> IJoinHandle<'a>

            member _.Dispose() = ()



[<RequireQualifiedAccess>]
module Schedulers =
    let threadPool: IScheduler = upcast new ThreadPoolImpl.ThreadPoolScheduler()


[<RequireQualifiedAccess>]
module Scheduler =

    let spawnOn (runtime: IScheduler) fut = runtime.Spawn(fut)
    let spawnOnThreadPool fut = spawnOn Schedulers.threadPool fut
