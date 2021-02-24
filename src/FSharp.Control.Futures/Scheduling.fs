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
module private SchedulerImpl =

    type IVarJoinHandle<'a>() =

        let inner: IVar<Result<'a, exn>> = IVar.create ()

        member inline _.Put(x) = inner.Put(x)

        interface IJoinHandle<'a> with

            member _.Join() =
                use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
                let context = { new Context() with member _.Wake() = wh.Set() |> ignore }

                let rec wait (current: Poll<Result<'a, exn>>) =
                    match current with
                    | Poll.Ready x -> x
                    | Poll.Pending ->
                        wh.WaitOne() |> ignore
                        wait (Future.Core.poll context inner)

                wait (Future.Core.poll context inner)

            member _.Poll(context) =
                let x = Future.Core.poll context inner
                match x with
                | Poll.Ready x ->
                    match x with
                    | Ok x -> Poll.Ready x
                    | Error e -> raise e
                | Poll.Pending -> Poll.Pending


    type ThreadPoolTask<'a>(future: Future<'a>, waiter: IVarJoinHandle<'a>) =
        let syncPoll = obj()

        member this.PushInThreadPool() =
            ThreadPool.QueueUserWorkItem(fun _ -> do this.Run()) |> ignore

        member this.Run() =
            lock syncPoll ^fun () ->
                let context =
                    { new Context() with
                        member _.Wake() =
                            lock syncPoll ^fun () ->
                                this.PushInThreadPool()
                    }
                try
                    let x = Future.Core.poll context future
                    match x with
                    | Poll.Ready x ->
                        waiter.Put(Ok x)
                    | Poll.Pending -> ()
                with e ->
                    waiter.Put(Error e)

    type ThreadPoolScheduler() =
        interface IScheduler with
            member this.Spawn(fut: Future<'a>) =
                let handle = IVarJoinHandle<'a>()
                let task = ThreadPoolTask<'a>(fut, handle)
                task.PushInThreadPool()
                handle :> IJoinHandle<'a>

            member _.Dispose() = ()



[<RequireQualifiedAccess>]
module Schedulers =
    let threadPool: IScheduler = upcast new SchedulerImpl.ThreadPoolScheduler()


[<RequireQualifiedAccess>]
module Scheduler =

    let spawnOn (runtime: IScheduler) fut = runtime.Spawn(fut)
    let spawnOnThreadPool fut = spawnOn Schedulers.threadPool fut
