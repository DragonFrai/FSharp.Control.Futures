namespace FSharp.Control.Futures.LowLevel

open System
open System.Threading
open FSharp.Control.Futures


exception MultipleNotifyException

[<RequireQualifiedAccess>]
module NotifyState =
    // for operations Poll(P), Cancel(C) and Notify(N) present next transitions on graphiz:
    // digraph G {
    //     I -> N [label = N]
    //     W -> N [label = N]
    //     T -> TN [label = N]
    //
    //     I -> W [label = P]
    //     N -> TN [label = P]
    //     W -> W [label = P]
    //
    //     I -> T [label = C]
    //     N -> TN [label = C]
    //     W -> T [label = C]
    //
    //     // T -> TerminatedEx [label = C]
    //     // TN -> TerminatedEx [label = C]
    //     // T -> TerminatedEx [label = P]
    //     // TN -> TerminatedEx [label = P]
    //     // N -> MultipleNotifyEx [label = N];
    //     // TN -> MultipleNotifyEx [label = N]
    // }

    // I -- Initialized
    // T -- Terminated: Polled with Ready or Canceled
    // W -- Waiting: has context
    // N -- Notified

    let [<Literal>] I = 0
    let [<Literal>] W = 1
    let [<Literal>] N = 2
    let [<Literal>] T = 3
    let [<Literal>] TN = 4

/// <summary>
/// The primitive for synchronizing ONE notifier and ONE notifiable.
/// SPSC (Single Producer Single Consumer)
/// </summary>
type [<Struct; NoComparison; NoEquality>] PrimaryNotify =
    val mutable _state: int
    val mutable _context: IContext

    new (isNotified: bool, isTerminated: bool) =
        let state =
            if isNotified then
                if isTerminated
                then NotifyState.TN
                else NotifyState.N
            else
                if isTerminated
                then NotifyState.T
                else NotifyState.I
        { _state = state; _context = nullObj }

    member inline this.IsInitOnly =
        let state = this._state
        state = NotifyState.I

    member inline this.IsWaiting =
        let state = this._state
        state = NotifyState.W

    member inline this.IsNotified =
        let state = this._state
        state = NotifyState.N || state = NotifyState.TN

    member inline this.IsTerminated =
        let state = this._state
        state = NotifyState.T || state = NotifyState.TN

    member inline this.TerminateForInit() =
        if this._state = NotifyState.I then
            this._state <- NotifyState.TN
        else
            raise (InvalidOperationException())

    // TODO: Replace bool to struct DU ?
    /// <summary>
    /// </summary>
    /// <returns> true if notification success, false if it was dropped </returns>
    /// <remarks>
    /// This can be used to undo the effect produced before the notification starts.
    /// For example, remove the previously set value.
    /// </remarks>
    member inline this.Notify() : bool =
        let mutable doLoop = true
        let mutable state = this._state
        let mutable result = false
        while doLoop do
            match state with
            | NotifyState.I ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.N, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- true
            | NotifyState.W ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.N, state)
                if state <> state' then state <- state'
                else
                    let ctx = this._context
                    this._context <- nullObj
                    ctx.Wake()
                    doLoop <- false; result <- true
            | NotifyState.T ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.TN, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- false
            | NotifyState.N
            | NotifyState.TN -> raise MultipleNotifyException
            | _ -> unreachable ()
        result

    /// <summary>
    /// Polls notify
    /// </summary>
    /// <param name="ctx"> Current async context </param>
    /// <returns> true if notified </returns>
    member inline this.Poll(ctx: IContext) : bool =
        let mutable doLoop = true
        let mutable state = this._state
        let mutable result = false
        while doLoop do
            match state with
            | NotifyState.I ->
                this._context <- ctx
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.W, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- false
            | NotifyState.W ->
                if this._context <> ctx then unreachable ()
                doLoop <- false; result <- false
            | NotifyState.N ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.TN, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- true
            | NotifyState.T
            | NotifyState.TN -> raise (FutureTerminatedException())
            | _ -> unreachable ()
        result

    /// <summary>
    /// Cancel waiting notification
    /// </summary>
    /// <returns> true if notified </returns>
    /// <remarks> The return value can be used to manually clean up external resources after cancellation </remarks>
    member inline this.Drop() : bool =
        let mutable doLoop = true
        let mutable result = false
        let mutable state = this._state
        while doLoop do
            match state with
            | NotifyState.I ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.T, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- false
            | NotifyState.W ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.T, state)
                if state <> state' then state <- state'
                else
                    this._context <- nullObj
                    doLoop <- false; result <- false
            | NotifyState.N ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.TN, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- true
            | NotifyState.T
            | NotifyState.TN -> raise (FutureTerminatedException())
            | _ -> unreachable ()
        result
