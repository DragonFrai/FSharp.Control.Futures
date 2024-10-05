namespace FSharp.Control.Futures.LowLevel.Runtime

open System
open System.Diagnostics
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.LowLevel.Runtime
open Microsoft.FSharp.Core


type FutureAbortedWithExceptionOnDrop(innerException: exn) =
    inherit Exception("Future task aborted with exception on drop", innerException)

[<RequireQualifiedAccess>]
[<Struct>]
type AwaiterState =
    | AwaitInForegroundMode
    | AwaitInBackgroundMode
    | NotAwaited
    | Terminated

[<Struct>]
[<RequireQualifiedAccess>]
type private FutureTaskResultState<'a> =
    | Pending
    | Ready of value: 'a
    | Failed of exn: exn
    | Aborted
    | Taken
    | Ignored

[<AbstractClass>]
type AbstractFutureTask<'a> =

    // Concurrent state
    val mutable private State: FutureTaskState

    // Not concurrent state
    val mutable private Future: NaiveFuture<'a>

    val mutable private IsStarted: bool
    val mutable private AwaiterState: AwaiterState
    val mutable private AwaiterContext: IContext

    val mutable private Result: FutureTaskResultState<'a>

    abstract member Schedule: unit -> unit
    abstract member Features: unit -> IFeatureProvider

    new(fut: Future<'a>) = {
        State = Unchecked.defaultof<_>
        IsStarted = false
        AwaiterState = AwaiterState.NotAwaited
        AwaiterContext = Unchecked.defaultof<_>
        Result = FutureTaskResultState.Pending
        Future = NaiveFuture(fut)
        // ScheduleFn = scheduleFn
    }

    // [RUNTIME_PART]
    // Runtime part context implementation.
    // This context must pass in spawned future and reschedule work.

    member inline private this.SetResultAndComplete(result: FutureTaskResultState<'a>): unit =
        this.Result <- result
        let r = this.State.TransitionRunningToComplete()
        match r with
        | TransitRunningToCompletedResult.OkAwaiterNotExists ->
            this.Result <- FutureTaskResultState.Ignored
        | TransitRunningToCompletedResult.OkAwaiterExistsWithContext ->
            this.AwaiterContext.Wake()
        | TransitRunningToCompletedResult.OkAwaiterExists ->
            ()
        ()

    /// <summary>
    /// Poll spawned future and notify awaiter if it exists.
    /// </summary>
    /// <returns>
    /// true, when future was ready or aborted.
    /// </returns>
    member this.DoWork(): bool =
        let r = this.State.TransitionIdleToRunning()
        match r with
        | TransitIdleToRunningResult.Ok ->
            try
                let r = this.Future.Poll(this :> IContext)
                match r with
                | NaivePoll.Ready value ->
                    this.SetResultAndComplete(FutureTaskResultState.Ready value)
                    true
                | NaivePoll.Pending ->
                    let r = this.State.TransitionRunningToIdle()
                    match r with
                    | TransitRunningToIdleResult.Ok ->
                        false
                    | TransitRunningToIdleResult.OkNotifyIsSet ->
                        this.Schedule()
                        false
                    | TransitRunningToIdleResult.ErrAbortIsSet ->
                        try
                            this.Future.Drop()
                            this.SetResultAndComplete(FutureTaskResultState.Aborted)
                        with ex ->
                            this.SetResultAndComplete(FutureTaskResultState.Failed ex)
                        true
            with ex ->
                this.SetResultAndComplete(FutureTaskResultState.Failed ex)
                true
        | TransitIdleToRunningResult.OkAbortIsSet ->
            try
                this.Future.Drop()
                this.SetResultAndComplete(FutureTaskResultState.Aborted)
            with ex ->
                // Drop invariant broken. Any behaviour will be correct.
                // But leaving exception inside runtime will hide error.
                // User anyway shouldn't build logic on strict exception set.
                let wrappedEx = FutureAbortedWithExceptionOnDrop(ex)
                this.SetResultAndComplete(FutureTaskResultState.Failed wrappedEx)
            true
        | TransitIdleToRunningResult.ErrAlreadyRunning ->
            failwith "DOUBLE WORK"


    // TODO: Add ignoring flag
    member this.Start(): unit =
        if this.IsStarted then raise (InvalidOperationException())
        this.IsStarted <- true

        this.State <- FutureTaskState.NewNotifiedWithExistsAwaiter

        do this.Schedule()



    interface IContext with
        member this.Wake(): unit =
            let r = this.State.SetNotify()
            if r then this.Schedule()

        member this.Features(): IFeatureProvider =
            this.Features()

    // [/RUNTIME_PART]


    // [AWAITER_PART]

    member inline private this.TakeResult(): AwaitResult<'a> =
        let prev = this.Result
        this.Result <- FutureTaskResultState.Taken
        match prev with
        | FutureTaskResultState.Ready value -> Ok value
        | FutureTaskResultState.Failed ex -> Error (AwaitError.Failed ex)
        | FutureTaskResultState.Aborted -> Error AwaitError.Aborted
        | FutureTaskResultState.Pending -> raise (UnreachableException("FutureTask result not ready yet."))
        | FutureTaskResultState.Taken -> raise (UnreachableException("FutureTask result already taken."))
        | FutureTaskResultState.Ignored -> raise (UnreachableException("FutureTask result ignored (awaiter not exists on completion)."))

    interface Future<Result<'a, AwaitError>> with
        member this.Poll(ctx: IContext): Poll<Result<'a, AwaitError>> =
            match this.AwaiterState with
            | AwaiterState.AwaitInForegroundMode
            | AwaiterState.AwaitInBackgroundMode ->
                if this.State.IsCompleted then
                    this.AwaiterState <- AwaiterState.Terminated
                    Poll.Ready (this.TakeResult())
                else
                    // NOTE: Safe, because only Awaiter part can write in field AwaiterContext
                    if isNotNull this.AwaiterContext then
                        // Double poll is correct behaviour. Assert context equality invariant.
                        if refNotEq this.AwaiterContext ctx then raise (InvalidOperationException())
                        Poll.Pending
                    else
                        this.AwaiterContext <- ctx
                        let r = this.State.SetAwaiterContext()
                        match r with
                        | SetAwaiterContextResult.Ok ->
                            Poll.Pending
                        | SetAwaiterContextResult.ErrAlreadyCompleted ->
                            this.AwaiterContext <- nullObj<IContext>
                            this.AwaiterState <- AwaiterState.Terminated
                            Poll.Ready (this.TakeResult())
            | AwaiterState.NotAwaited ->
                raise (InvalidOperationException("FutureTask awaiter not awaited."))
            | AwaiterState.Terminated ->
                raise (FutureTerminatedException("FutureTask awaiter already terminated."))

        member this.Drop(): unit =
            match this.AwaiterState with
            | AwaiterState.AwaitInForegroundMode ->
                this.AwaiterState <- AwaiterState.Terminated
                let r = this.State.SetAbortAndNotify()
                if r then this.Schedule()
            | AwaiterState.AwaitInBackgroundMode ->
                this.AwaiterState <- AwaiterState.Terminated
                let r = this.State.UnsetAwaiterExists()
                match r with
                | UnsetAwaiterExistsResult.Ok -> ()
                | UnsetAwaiterExistsResult.OkContextIsSet -> this.AwaiterContext <- nullObj
            | AwaiterState.NotAwaited ->
                raise (InvalidOperationException("FutureTask awaiter not awaited."))
            | AwaiterState.Terminated ->
                raise (FutureTerminatedException("FutureTask awaiter already terminated."))

    // [/AWAITER_PART]

    interface IFutureTask<'a> with
        member this.Await(background: bool): Future<AwaitResult<'a>> =
            if this.AwaiterState <> AwaiterState.NotAwaited then
                raise (FutureTaskMultipleAwaitException())

            this.AwaiterState <-
                if background
                then AwaiterState.AwaitInBackgroundMode
                else AwaiterState.AwaitInForegroundMode

            this :> Future<Result<'a, AwaitError>>

        member this.Await(): Future<AwaitResult<'a>> =
            (this :> IFutureTask<'a>).Await(false)

        member this.Abort(): unit =
            let r = this.State.SetAbortAndNotify()
            if r then this.Schedule()

