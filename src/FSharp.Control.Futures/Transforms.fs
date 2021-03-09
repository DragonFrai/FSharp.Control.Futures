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

        let ofAsync (x: Async<'a>) : Future<'a> =
            let mutable result = AsyncResult.Pending
            let mutable started = false
            Future.Core.create
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

            Future.Core.create
            <| fun context ->
                let pollResult = Future.Core.poll context ivar
                match pollResult with
                | Poll.Ready result ->
                    match result with
                    | Ok x -> Poll.Ready x
                    | Error ex -> raise ex
                | Poll.Pending -> Poll.Pending
            <| fun () ->
                // TODO
                (ivar :> Future<_>).Cancel()

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
