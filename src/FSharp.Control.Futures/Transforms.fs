[<AutoOpen>]
module FSharp.Control.Futures.Transforms

open System
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Core.Utils


[<AutoOpen>]
module FutureAsyncTransforms =

    [<RequireQualifiedAccess>]
    module AsyncComputation =

        [<RequireQualifiedAccess>]
        type AsyncResult<'a> =
            | Pending
            | Completed of 'a
            | Errored of exn
            | Cancelled of OperationCanceledException

        let ofAsync (x: Async<'a>) : IAsyncComputation<'a> =
            let mutable result = AsyncResult.Pending
            let mutable started = false
            AsyncComputation.create
            <| fun context ->
                if not started then
                    started <- true
                    Async.StartWithContinuations(
                        x,
                        (fun r -> result <- AsyncResult.Completed r; context.Wake()),
                        (fun e -> result <- AsyncResult.Errored e; context.Wake()),
                        (fun ec -> result <- AsyncResult.Cancelled ec; context.Wake())
                    )
                match result with
                | AsyncResult.Pending -> Poll.Pending
                | AsyncResult.Completed result -> Poll.Ready result
                | AsyncResult.Cancelled ec -> raise ec //Poll.Ready ^ MaybeCancel.Cancelled ec
                | AsyncResult.Errored e -> raise e
            <| fun () ->
                // todo: impl
                do ()

        let toAsync (fut: IAsyncComputation<'a>) : Async<'a> =
            // TODO: notify Async based awaiter about Future cancellation

            let wh = new EventWaitHandle(false, EventResetMode.AutoReset)
            let ctx =
                { new IContext with
                    member _.Wake() = wh.Set() |> ignore
                    member _.Scheduler = None
                }

            let rec wait () =
                let current = AsyncComputation.poll ctx fut
                match current with
                | Poll.Ready x -> async { return x }
                | Poll.Pending -> async {
                    let _whr = Async.AwaitWaitHandle(wh)
                    return! wait ()
                }

            async {
                return! wait ()
            }


[<AutoOpen>]
module FutureTaskTransforms =

    [<RequireQualifiedAccess>]
    module AsyncComputation =

        open System.Threading.Tasks


        let ofTask (task: Task<'a>) : IAsyncComputation<'a> =
            let ivar = OnceVar.create ()

            task.ContinueWith(fun (task: Task<'a>) ->
                let taskResult =
                    if task.IsFaulted then Error task.Exception
                    elif task.IsCanceled then Error task.Exception
                    elif task.IsCompletedSuccessfully then Ok task.Result
                    else invalidOp "Unreachable"
                OnceVar.write taskResult ivar
            ) |> ignore

            AsyncComputation.create
            <| fun context ->
                let pollResult = AsyncComputation.poll context ivar
                match pollResult with
                | Poll.Ready result ->
                    match result with
                    | Ok x -> Poll.Ready x
                    | Error ex -> raise ex
                | Poll.Pending -> Poll.Pending
            <| fun () ->
                // TODO
                (ivar :> IAsyncComputation<_>).Cancel()

        // TODO: Implement without blocking
        let toTask (x: IAsyncComputation<'a>) : Task<'a> =
            Task<'a>.Factory.StartNew(
                fun () ->
                    x |> AsyncComputation.runSync
            )

        // TODO: Implement without blocking
        let toTaskOn (scheduler: TaskScheduler) (x: IAsyncComputation<'a>) : Task<'a> =
            TaskFactory<'a>(scheduler).StartNew(
                fun () ->
                    x |> AsyncComputation.runSync
            )
