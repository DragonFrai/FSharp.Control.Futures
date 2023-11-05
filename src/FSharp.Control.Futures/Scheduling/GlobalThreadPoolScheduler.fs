namespace rec FSharp.Control.Futures.Scheduling

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Scheduling.RunnerScheduler.RunnerScheduler


type GlobalThreadPoolTaskRunner() =
    interface ITaskRunner with
        member _.RunTask(task) =
            ThreadPool.QueueUserWorkItem(fun _ -> do task.Run()) |> ignore

type GlobalThreadPoolScheduler internal () =
    interface IScheduler with
        member this.Spawn(fut: Future<'a>) =
            let task = RunnerTask<'a>(fut, GlobalThreadPoolScheduler.globalThreadPoolTaskRunner)
            task.InitialRun()
            task :> IFutureTask<'a>
        member _.Dispose() = ()

module GlobalThreadPoolScheduler =
    let internal globalThreadPoolTaskRunner = GlobalThreadPoolTaskRunner()
    let globalThreadPoolScheduler: IScheduler = upcast new GlobalThreadPoolScheduler()
