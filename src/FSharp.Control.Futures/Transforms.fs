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
            let cts = new CancellationTokenSource()
            AsyncComputation.create
            <| fun context ->
                if not started then
                    started <- true
                    Async.StartWithContinuations(
                        x,
                        (fun r -> result <- AsyncResult.Completed r; context.Wake()),
                        (fun e -> result <- AsyncResult.Errored e; context.Wake()),
                        (fun ec -> result <- AsyncResult.Cancelled ec; context.Wake()),
                        cts.Token
                    )
                match result with
                | AsyncResult.Pending -> Poll.Pending
                | AsyncResult.Completed result -> Poll.Ready result
                | AsyncResult.Cancelled ec -> raise ec //Poll.Ready ^ MaybeCancel.Cancelled ec
                | AsyncResult.Errored e -> raise e
            <| fun () ->
                cts.Cancel()

        let toAsync (fut: IAsyncComputation<'a>) : Async<'a> =
            // TODO: notify Async based awaiter about Future cancellation

            let wh = new EventWaitHandle(false, EventResetMode.AutoReset)
            let ctx =
                { new IContext with
                    member _.Wake() = wh.Set() |> ignore
                    member _.Scheduler = None
                }

            let mutable fut = fut

            let rec wait () =
                AsyncComputation.Helpers.PollTransiting(&fut, ctx
                , onReady=fun x -> async { return x }
                , onPending=fun () ->
                    async {
                        let! _whr = Async.AwaitWaitHandle(wh)
                        return! wait ()
                    }
                )

            async {
                let! disp = Async.OnCancel(fun () -> fut.Cancel())
                let! r = wait ()
                disp.Dispose()
                return r
            }

module FutureApmTransforms =
    [<RequireQualifiedAccess>]
    module AsyncComputation =

        type private FutureAsyncResult<'a>(state: obj) =
            let asyncWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset)
            let mutable result: 'a option = None

            member this.SetComplete(r: 'a) =
                result <- Some r
                asyncWaitHandle.Set() |> ignore

            member this.Result =
                match result with
                | None -> invalidOp "Is not completed yet"
                | Some result -> result

            member this.AsyncWaitHandle = asyncWaitHandle
            member this.IsCompleted = Option.isSome result
            interface IAsyncResult with
                member this.AsyncState = state
                member this.AsyncWaitHandle = upcast this.AsyncWaitHandle
                member this.CompletedSynchronously = false
                member this.IsCompleted = this.IsCompleted

            member this.Dispose() =
                asyncWaitHandle.Dispose()
            interface IDisposable with member this.Dispose() = this.Dispose()

        let toBeginEnd (startPoll: (unit -> unit) -> unit) (fut: IAsyncComputation<'a>)
            : {| Begin: AsyncCallback -> obj -> IAsyncResult
                 End: IAsyncResult -> 'a |} =
            let beginMethod (callback: AsyncCallback) (state: obj) : IAsyncResult =
                let asyncResult = new FutureAsyncResult<'a>(state)

                let mutable fut = fut
                let startPollOnContext (ctx: IContext) =
                    startPoll (fun () ->
                        AsyncComputation.Helpers.pollTransiting fut ctx
                        <| fun result ->
                            asyncResult.SetComplete(result)
                            if isNotNull callback then callback.Invoke(asyncResult)
                        <| fun () -> ()
                        <| fun f -> fut <- f
                    )

                let ctx =
                    { new IContext with
                        member this.Wake() =
                            if asyncResult.IsCompleted then invalidOp "Cannot call Wait when Future is Ready"
                            startPollOnContext this
                        member _.Scheduler = None }

                startPollOnContext ctx

                upcast asyncResult

            let endMethod (asyncResult: IAsyncResult) : 'a =
                let asyncResult = asyncResult :?> FutureAsyncResult<'a>
                asyncResult.AsyncWaitHandle.WaitOne() |> ignore
                asyncResult.Dispose()
                asyncResult.Result

            {| Begin = beginMethod; End = endMethod |}


[<AutoOpen>]
module FutureTaskTransforms =

    [<RequireQualifiedAccess>]
    module AsyncComputation =

        open System.Threading.Tasks
        open FutureApmTransforms

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

            ivar
            |> AsyncComputation.map (function Ok x -> x | Error ex -> raise ex)

        let toTaskOn (scheduler: TaskScheduler) (fut: IAsyncComputation<'a>) : Task<'a> =
            let pollingTaskFactory = TaskFactory(scheduler)
            let startPoll poll = pollingTaskFactory.StartNew(fun () -> poll ()) |> ignore
            let beginEnd = AsyncComputation.toBeginEnd startPoll fut
            let beginMethod = beginEnd.Begin
            let endMethod = beginEnd.End
            let factory = TaskFactory<'a>(scheduler)
            factory.FromAsync(beginMethod, endMethod, null)

        let toTask (fut: IAsyncComputation<'a>) : Task<'a> =
            let scheduler = Task.Factory.Scheduler
            toTaskOn scheduler fut
