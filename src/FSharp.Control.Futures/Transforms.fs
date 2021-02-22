[<AutoOpen>]
module FSharp.Control.Futures.Transforms

open System
open FSharp.Control.Futures


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

        // TODO: Return
//        let ofTask (x: Task<'a>) : Future<'a> =
//            let ch = Watch.create ()
//            x.ContinueWith(fun (task: Task<'a>) -> task.Result |> Sender.send ch; ch.Dispose()) |> ignore
//            Receiver.receive ch |> Future.map (function Ok x -> x | _ -> invalidOp "")

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
