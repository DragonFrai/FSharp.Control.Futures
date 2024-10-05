namespace FSharp.Control.Futures.Runtime

open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.LowLevel.Runtime


[<Class>]
[<Sealed>]
type private ThreadPoolFutureTask<'a>(fut: Future<'a>) =
    inherit AbstractFutureTask<'a>(fut)

    override this.Schedule(): unit =
        let work (task: ThreadPoolFutureTask<'a>) =
            task.DoWork() |> ignore
        let successfulQueued = ThreadPool.QueueUserWorkItem(work, this, false)
        if not successfulQueued then
            let msg = "System.Threading.ThreadPool.QueueUserWorkItem returns false"
            raise (UnreachableException(msg))

    override this.Features() = EmptyFeatureProvider.Instance

[<Class>]
[<Sealed>]
type ThreadPoolRuntime private () =
    static member Instance: IRuntime = ThreadPoolRuntime()

    interface IRuntime with
        member this.Spawn(fut): IFutureTask<'a> =
            let task = ThreadPoolFutureTask(fut)
            do task.Start()
            task :> IFutureTask<'a>

[<RequireQualifiedAccess>]
module ThreadPoolRuntime =
    let instance = ThreadPoolRuntime.Instance
    let inline spawn fut = Runtime.spawn ThreadPoolRuntime.Instance fut
