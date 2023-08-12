namespace FSharp.Control.Futures.Scheduling

open System.Threading

open FSharp.Control.Futures
open FSharp.Control.Futures.Types
open FSharp.Control.Futures.Internals
open FSharp.Control.Futures.Sync


// -------------------
// ThreadPollScheduler

module internal rec RunnerScheduler =

    // Бит сигнализирующий о наличии пробуждения
    let IsWakedBit: uint64 = (1uL <<< 1)
    // Бит сигнализирующий о запуске через раннер
    let IsRunBit: uint64 = (1uL <<< 2)
    // Бит окончания выполнения
    let IsCompleteBit: uint64 = (1uL <<< 3)

    type ITaskRunner =
        abstract RunTask: RunnerTask<'a> -> unit
        abstract Scheduler: IScheduler option

    type RunnerTask<'a>(fut: Future<'a>, runner: ITaskRunner) as this =

        let mutable ivar = IVar<'a>()
        let mutable state = 0uL

        let mutable fut = fut

        let changeState (f: uint64 -> uint64) : uint64 =
            let mutable prevState = 0uL
            let inline tryUpdate () =
                let currentState = state
                let toSet = f currentState
                prevState <- Interlocked.CompareExchange(&state, toSet, currentState)
                prevState <> currentState
            while tryUpdate () do ()
            prevState

        let context =
            { new IContext with
                member _.Wake() =
                    // Запускает таску через раннер, если она еще не запущена и не выполнена и устанавливает бит пробуждения
                    let prevState = changeState (fun x -> x ||| IsWakedBit ||| IsRunBit)
                    let alreadyRun = (prevState &&& IsRunBit) <> 0uL
                    let complete = (prevState &&& IsCompleteBit) <> 0uL
                    let shouldRun = ((not alreadyRun) && (not complete))
                    if shouldRun then runner.RunTask(this)
                override _.Scheduler = runner.Scheduler
            }

        // Метод обработки таски. Засчет флагов не должен вызываться одновременно более 1 раза
        member this.Run() =
            // Сбрасывает бит запроса обновления, сигнализируя о опросе
            let mutable prevState = changeState (fun x -> x &&& (~~~IsWakedBit))

            let mutable isComplete = false
            try
                PollTransiting(&fut, context
                , onReady=fun x ->
                    IVar.writeValue x ivar
                    isComplete <- true
                    prevState <- changeState (fun x -> x ||| IsCompleteBit)
                , onPending=fun () -> ()
                )
            with e ->
                IVar.writeFailure e ivar

            let isWaked = (prevState &&& IsWakedBit) <> 0uL
            if isWaked && not isComplete
            then runner.RunTask(this)
            else changeState (fun x -> x &&& (~~~IsRunBit)) |> ignore

        member this.InitialRun() =
            changeState (fun x -> x ||| IsRunBit ||| IsWakedBit) |> ignore
            runner.RunTask(this)

        interface IJoinHandle<'a> with

            member _.Await() =
                ivar.Read()

            member _.Join() =
                ivar.Read() |> Future.runSync

            member _.Cancel() =
                ivar |> IVar.writeFailure FutureCancelledException


    type GlobalThreadPoolTaskRunner() =
        interface ITaskRunner with
            member _.RunTask(task) =
                ThreadPool.QueueUserWorkItem(fun _ -> do task.Run()) |> ignore
            member _.Scheduler = Some globalThreadPoolScheduler

    type GlobalThreadPoolScheduler() =
        interface IScheduler with
            member this.Spawn(fut: Future<'a>) =
                let task = RunnerTask<'a>(fut, globalThreadPoolTaskRunner)
                task.InitialRun()
                task :> IJoinHandle<'a>

            member _.Dispose() = ()

    let globalThreadPoolTaskRunner = GlobalThreadPoolTaskRunner()
    let globalThreadPoolScheduler: IScheduler = upcast new GlobalThreadPoolScheduler()


[<RequireQualifiedAccess>]
module Schedulers =
    let threadPool: IScheduler = RunnerScheduler.globalThreadPoolScheduler


[<RequireQualifiedAccess>]
module Scheduler =

    /// <summary> Run Future on passed scheduler </summary>
    /// <returns> Return Future waited result passed Future </returns>
    let spawnOn (scheduler: IScheduler) (fut: Future<'a>) = scheduler.Spawn(fut)
    /// <summary> Run Future on thread pool scheduler </summary>
    /// <returns> Return Future waited result passed Future </returns>
    let spawnOnThreadPool fut = spawnOn Schedulers.threadPool fut
