namespace FSharp.Control.Futures.Scheduling

open System
open System.Threading
open FSharp.Control.Futures

/// <summary> Introduces the API to the top-level future. </summary>
/// <remarks> Can be safely canceled </remarks>
type IJoinHandle<'a> =
    inherit IComputation<'a>
    abstract member Join: unit -> Result<'a, exn>

/// <summary> Future scheduler. </summary>
type IScheduler =
    // The scheduler may contain an internal thread pool and other system resources that should be cleaned up
    inherit IDisposable
    /// <summary> Run Future on this scheduler </summary>
    /// <returns> Return Future waited result passed Future </returns>
    abstract Spawn: fut: IComputation<'a> -> IJoinHandle<'a>


// -------------------
// ThreadPollScheduler
// -------------------
module private rec ThreadPoolImpl =

    let private addTaskToThreadPoolQueue (task: ThreadPoolTask<'a>) =
        ThreadPool.QueueUserWorkItem(fun _ -> do task.Run()) |> ignore

    type ThreadPoolTask<'a>(future: IComputation<'a>) as this =

        let mutable isComplete = false
        let mutable isRequireWake = false // init with require to update
        let mutable isInQueue = false

        let waiter: OnceVar<Result<'a, exn>> = OnceVar.create ()

        let sync = obj()

        let context =
            { new Context() with
                member _.Wake() =
                    lock sync <| fun () ->
                        isRequireWake <- true
                        if not isInQueue then
                            addTaskToThreadPoolQueue this
                            isInQueue <- true
            }

        member this.Run() =
            let isComplete' =
                lock sync <| fun () ->
                    isRequireWake <- false
                    isComplete

            if isComplete' then ()
            else
            try
                let x = Computation.poll context future
                match x with
                | Poll.Ready x ->
                    waiter.Put(Ok x)
                    lock sync ^fun () ->
                        isComplete <- true
                | Poll.Pending -> ()
            with e ->
                waiter.Put(Error e)

            lock sync <| fun () ->
                if isRequireWake && not isComplete
                then addTaskToThreadPoolQueue this
                else isInQueue <- false

        member _.InitForQueue() =
            isInQueue <- true
            isRequireWake <- true

        interface IJoinHandle<'a> with

            member _.Join() =
                Computation.runSync waiter

            member _.Poll(context) =
                let x = Computation.poll context waiter
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
            member this.Spawn(fut: IComputation<'a>) =
                let task = ThreadPoolTask<'a>(fut)
                task.InitForQueue()
                addTaskToThreadPoolQueue task
                task :> IJoinHandle<'a>

            member _.Dispose() = ()



[<RequireQualifiedAccess>]
module Schedulers =
    let threadPool: IScheduler = upcast new ThreadPoolImpl.ThreadPoolScheduler()


[<RequireQualifiedAccess>]
module Scheduler =

    /// <summary> Run Future on passed scheduler </summary>
    /// <returns> Return Future waited result passed Future </returns>
    let spawnOn (scheduler: IScheduler) fut = scheduler.Spawn(fut)
    /// <summary> Run Future on thread pool scheduler </summary>
    /// <returns> Return Future waited result passed Future </returns>
    let spawnOnThreadPool fut = spawnOn Schedulers.threadPool fut
