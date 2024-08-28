namespace rec FSharp.Control.Futures.Runtime.ThreadPoolRuntime

open System
open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Runtime.LowLevel


[<Class>]
type ThreadPoolFutureTask<'a>(fut: Future<'a>) =
    inherit AbstractFutureTask<'a>(fut)

    override this.Schedule(): unit =
        let work (task: ThreadPoolFutureTask<'a>) =
            task.DoWork() |> ignore
        let successfulQueued = ThreadPool.QueueUserWorkItem(work, this, false)
        if not successfulQueued then
            let msg = "System.Threading.ThreadPool.QueueUserWorkItem returns false"
            raise (UnreachableException(msg))


[<Class>]
type ThreadPoolRuntime private () =

    static member Instance: IRuntime = new ThreadPoolRuntime()

    interface IRuntime with
        member this.Dispose() =
            let tyName = nameof(ThreadPoolRuntime)
            let msg = $"{tyName} is a global singleton and cannot be disposed"
            raise (InvalidOperationException(msg))

        member this.Spawn(fut): IFutureTask<'a> =
            let task = ThreadPoolFutureTask(fut)
            do task.Start()
            task :> IFutureTask<'a>
