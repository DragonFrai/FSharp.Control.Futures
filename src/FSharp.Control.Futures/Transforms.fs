[<AutoOpen>]
module FSharp.Control.Futures.Transforms

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


[<AutoOpen>]
module FutureAsyncTransforms =

    [<RequireQualifiedAccess>]
    module Future =

        [<RequireQualifiedAccess>]
        type AsyncResult<'a> =
            | Pending
            | Completed of 'a
            | Errored of exn
            | Cancelled of OperationCanceledException

        let ofAsync (x: Async<'a>) : CancellableFuture<'a> =
            let mutable result = AsyncResult.Pending
            let mutable started = false
            Future.Core.create ^fun waker ->
                if not started then
                    started <- true
                    Async.StartWithContinuations(
                        x,
                        (fun r -> result <- AsyncResult.Completed r; waker ()),
                        (fun e -> result <- AsyncResult.Errored e; waker ()),
                        (fun ec -> result <- AsyncResult.Cancelled ec; waker ())
                    )
                match result with
                | AsyncResult.Pending -> Poll.Pending
                | AsyncResult.Completed result -> Poll.Ready ^ MaybeCancel.Completed result
                | AsyncResult.Cancelled ec -> Poll.Ready ^ MaybeCancel.Cancelled ec
                | AsyncResult.Errored e -> raise e

        // TODO: Implement without blocking
        let toAsync (x: Future<'a>) : Async<'a> =
            async {
                let r = x |> Future.runSync
                return r
            }


[<AutoOpen>]
module FutureTaskTransforms =

    [<RequireQualifiedAccess>]
    module Future =

        open System.Threading.Tasks


        let ofTask (task: Task<'a>) : Future<'a> =
            let ivar = IVar.create ()

            task.ContinueWith(fun (task: Task<'a>) ->
                let taskResult =
                    if task.IsFaulted then Error task.Exception
                    elif task.IsCanceled then Error task.Exception
                    elif task.IsCompletedSuccessfully then Ok task.Result
                    else invalidOp "Unreachable"
                IVar.put taskResult ivar
            ) |> ignore

            Future.Core.create ^fun waker ->
                let pollResult = Future.Core.poll waker ivar
                match pollResult with
                | Poll.Ready result ->
                    match result with
                    | Ok x -> Poll.Ready x
                    | Error ex -> raise ex
                | Poll.Pending -> Poll.Pending

        // TODO: Implement without blocking
        let toTask (x: Future<'a>) : Task<'a> =
            Task<'a>.Factory.StartNew(
                fun () ->
                    x |> Future.runSync
            )

        // TODO: Implement without blocking
        let toTaskOn (scheduler: TaskScheduler) (x: Future<'a>) : Task<'a> =
            TaskFactory<'a>(scheduler).StartNew(
                fun () ->
                    x |> Future.runSync
            )
