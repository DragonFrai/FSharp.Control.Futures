[<AutoOpen>]
module FSharp.Control.Futures.Transforms

open System
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Types
open FSharp.Control.Futures.Internals


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

        type AsyncFuture<'a>(asyncWorkflow: Async<'a>) =
            let mutable result = AsyncResult.Pending
            let mutable started = false
            let cts = new CancellationTokenSource()
            interface Future<'a> with
                member _.Poll(ctx) =
                    if not started then
                        started <- true
                        Async.StartWithContinuations(
                            asyncWorkflow,
                            (fun r -> result <- AsyncResult.Completed r; ctx.Wake()),
                            (fun e -> result <- AsyncResult.Errored e; ctx.Wake()),
                            (fun ec -> result <- AsyncResult.Cancelled ec; ctx.Wake()),
                            cts.Token
                        )
                    match result with
                    | AsyncResult.Pending -> Poll.Pending
                    | AsyncResult.Completed result -> Poll.Ready result
                    | AsyncResult.Cancelled ec -> raise ec //Poll.Ready ^ MaybeCancel.Cancelled ec
                    | AsyncResult.Errored e -> raise e
                member _.Cancel() =
                    cts.Cancel()
                    ()

        let ofAsync (asyncWorkflow: Async<'a>) : Future<'a> =
            upcast AsyncFuture<'a>(asyncWorkflow)

        let toAsync (fut: Future<'a>) : Async<'a> =
            // TODO: notify Async based awaiter about Future cancellation

            let wh = new EventWaitHandle(false, EventResetMode.AutoReset)
            let ctx =
                { new IContext with
                    member _.Wake() = wh.Set() |> ignore
                    member _.Scheduler = None
                }

            let mutable fut = fut

            let rec wait () =
                PollTransiting(&fut, ctx
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
    module Future =

        type ApmFuture<'a>(beginMethod: AsyncCallback -> obj -> IAsyncResult, endMethod: IAsyncResult -> 'a) =
            let mutable asyncResult: IAsyncResult = Unchecked.defaultof<_>
            let mutable lastContext: IContext = Unchecked.defaultof<_>
            let asyncCallback =
                AsyncCallback(fun ar ->
                    asyncResult <- ar
                    lastContext.Wake()
                )

            interface Future<'a> with
                member this.Poll(ctx) =
                    lastContext <- ctx // FIXME: Context sync
                    // If is not started
                    if isNull asyncResult then
                        asyncResult <- beginMethod asyncCallback null
                        if asyncResult.CompletedSynchronously then
                            Poll.Ready (endMethod asyncResult)
                        else
                            Poll.Pending
                    elif not asyncResult.IsCompleted then
                        Poll.Pending
                    else
                        Poll.Ready (endMethod asyncResult)

                member this.Cancel() =
                    raise (NotSupportedException("APM based Futures don't support cancellation"))

        let ofBeginEnd (beginMethod: AsyncCallback -> obj -> IAsyncResult) (endMethod: IAsyncResult -> 'a) : Future<'a> =
            upcast ApmFuture<'a>(beginMethod, endMethod)

        // ----

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

        let toBeginEnd (startPoll: (unit -> unit) -> unit) (fut: Future<'a>)
            : {| Begin: AsyncCallback -> obj -> IAsyncResult
                 End: IAsyncResult -> 'a |} =
            let beginMethod (callback: AsyncCallback) (state: obj) : IAsyncResult =
                let asyncResult = new FutureAsyncResult<'a>(state)

                let mutable fut = fut
                let startPollOnContext (ctx: IContext) =
                    startPoll (fun () ->
                        pollTransiting fut ctx
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

    open System.Threading.Tasks
    open FutureApmTransforms

    [<RequireQualifiedAccess>]
    module Future =

        open FSharp.Control.Futures.Sync

        let ofTask (task: Task<'a>) : Future<'a> =
            let ivar = IVar.create ()

            task.ContinueWith(fun (task: Task<'a>) ->
                let taskResult =
                    if task.IsFaulted then Error task.Exception
                    elif task.IsCanceled then Error task.Exception
                    elif task.IsCompletedSuccessfully then Ok task.Result
                    else invalidOp "Unreachable"
                IVar.writeValue taskResult ivar
            ) |> ignore

            ivar
            |> IVar.read
            |> Future.map (function Ok x -> x | Error ex -> raise ex)

        let toTaskOn (scheduler: TaskScheduler) (fut: Future<'a>) : Task<'a> =
            let pollingTaskFactory = TaskFactory(scheduler)
            let startPoll poll = pollingTaskFactory.StartNew(fun () -> poll ()) |> ignore
            let beginEnd = Future.toBeginEnd startPoll fut
            let beginMethod = beginEnd.Begin
            let endMethod = beginEnd.End
            let factory = TaskFactory<'a>(scheduler)
            factory.FromAsync(beginMethod, endMethod, null)

        let toTask (fut: Future<'a>) : Task<'a> =
            let scheduler = Task.Factory.Scheduler
            toTaskOn scheduler fut
