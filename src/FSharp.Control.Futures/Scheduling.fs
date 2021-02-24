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

    type IVarJoinHandle<'a>() =

        let inner: IVar<Result<'a, exn>> = IVar.create ()

        member inline _.Put(x) = inner.Put(x)

        interface IJoinHandle<'a> with

            member _.Join() =
                Future.runSync inner

            member _.Poll(context) =
                let x = Future.Core.poll context inner
                match x with
                | Poll.Ready x ->
                    match x with
                    | Ok x -> Poll.Ready x
                    | Error e -> raise e
                | Poll.Pending -> Poll.Pending


    type ThreadPoolTask<'a>(future: Future<'a>, waiter: IVarJoinHandle<'a>) =

        // 0 -- false ; 1 -- true
        let mutable isRequireWake = true // init with require to update
        // 0 -- false ; 1 -- true
        let mutable isInQueue = false

        let sync = obj()

        member this.Run() =
            lock sync ^fun () ->
                isRequireWake <- false

            let context =
                { new Context() with
                    member _.Wake() =
                        lock sync ^fun () ->
                            isRequireWake <- true
                            if not isInQueue then
                                Utils.queueTask this
                }

            try
                let x = Future.Core.poll context future
                match x with
                | Poll.Ready x -> waiter.Put(Ok x)
                | Poll.Pending -> ()
            with e ->
                waiter.Put(Error e)

            lock sync ^fun () ->
                if isRequireWake
                then Utils.queueTask this
                else isInQueue <- false

    type ThreadPoolScheduler() =
        interface IScheduler with
            member this.Spawn(fut: Future<'a>) =
                let handle = IVarJoinHandle<'a>()
                let task = ThreadPoolTask<'a>(fut, handle)
                Utils.queueTask task
                handle :> IJoinHandle<'a>

            member _.Dispose() = ()



[<RequireQualifiedAccess>]
module Schedulers =
    let threadPool: IScheduler = upcast new ThreadPoolImpl.ThreadPoolScheduler()


[<RequireQualifiedAccess>]
module Scheduler =

    let spawnOn (runtime: IScheduler) fut = runtime.Spawn(fut)
    let spawnOnThreadPool fut = spawnOn Schedulers.threadPool fut
