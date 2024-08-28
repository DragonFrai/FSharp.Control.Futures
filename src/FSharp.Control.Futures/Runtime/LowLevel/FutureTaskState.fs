namespace FSharp.Control.Futures.Runtime.LowLevel

open System
open System.Threading
open Microsoft.FSharp.Core


/// <summary>
/// Base implementation of FutureTask
/// This module provides state bits declarations.
///
/// FutureTask можно разбить на 3 основных состояния:
/// - Idle -- Задача не выполняется, но еще не завершена
/// - Running -- Задача еще не завершена, но обрабатывается планировщиком прямо сейчас
/// - Completed -- Задача завершена и не выполняется.
///
/// Эти состояние представлены в первых 2-х битах, RunningBit и CompletedBit
/// Idle 0b00, Running 0b01, Completed 0b10.
/// Состояние Completed терминально (не может меняться после установки), а комбинация битов 0b11 - недопустима.
///
/// Другие биты:
/// - NotifiedBit -- Говорит о том, что задача была уведомлена о пробуждении и должна быть исполнена планировщиком.
///   * Установка бита возможна только в состояниях Idle и Running.
///   * Сброс бита происходит при переходе Idle -> Running
///   * Установка бита в состоянии Running+AbortBit не не имеет смысла.
///     Но не противоресива, т.к. будет отвергнута из-за Complete.
/// - AbortBit -- Говорит о том, что была запрошена отмена задачи.
///   * Уствновка бита допустима в состояниях Idle и Running.
///   * Если бит установлен, а состояние задачи Completed, задача считается Completed через Abort,
///     результат не существует
///   * Сброс бита не предполагается: если после состоянрия Running (с Polling'ом Future) произошел запрос на Abort,
///     и Poll дал результат Poll.Ready или выбросил исключение, предполагается отброс результата и сохранение состояния
///     Abort.
///   * Установка бита также подразумевает установку NotifiedBit с добавлением в очередь для опроса.
///     (NOTE: Возможен дизайн игнорирующий это, но тогда отмена будет работоспособна только в точках прерывания.)
///
/// Следующая схема graphviz отражает возможные переходы (без учета AwaiterContextBit):
/// ```
/// digraph G {
///     Idle -> IdleAbortNotified [label=abort]
///     Idle -> IdleNotified [label=notify]
///     IdleNotified -> Running [label=running]
///     IdleAbortNotified -> RunningAbort [label=running_abort]
///     RunningAbort -> CompletedAbort [label=aborted]
///     Running -> RunningNotified [label=notify]
///     RunningNotified -> IdleNotified [label=complete_pending]
///     RunningNotified -> Completed [label=complete_ready]
///     RunningNotified -> RunningAbortNotified [label=abort]
///     Running -> Idle [label=complete_pending]
///     Running -> Completed [label=complete_ready]
///     Running -> RunningAbortNotified [label=abort]
///     IdleNotified -> IdleAbortNotified [label=abort]
///     RunningAbortNotified -> CompletedAbort [label=complete_any_and_running_abort]
/// }
/// ```
///
/// NotifiedBit and AbortBit can be set only in Idle and Running states.
/// AwaiterExistsBit and AwaiterContextBit can be set in any states (Idle, Running, Complete).
///
/// === THIS DOCUMENTATION PARTIALLY ACTUAL NOW ===
/// </remarks>
[<RequireQualifiedAccess>]
module FutureTaskState =

    /// Future completed (Poll.Ready, exn, aborted)
    let [<Literal>] CompletedBit      = 0b0000_0001

    /// Flag, tracking running task on runtime thread
    let [<Literal>] RunningBit        = 0b0000_0010

    /// Flag, tracking if Future was set for aborting
    let [<Literal>] AbortBit          = 0b0000_0100

    /// Flag, tracked if FutureTask must be handled by runtime
    let [<Literal>] NotifiedBit       = 0b0000_1000

    /// Flag, tracking if result awaiting Future context has been set
    let [<Literal>] AwaiterContextBit = 0b0001_0000

    /// Flag, tracking if await future not dropped
    let [<Literal>] AwaiterExistsBit  = 0b0010_0000

    // [Initial states]
    let [<Literal>] Initial = 0b0000_0000
    let [<Literal>] InitialNotified = 0b0000_1000
    let [<Literal>] InitialNotifiedWithAwaiterExists = 0b0010_1000

    let inline isIdle (s: int) : bool = (s &&& (CompletedBit ||| RunningBit)) = 0
    let inline isRunning (s: int) : bool = (s &&& RunningBit) <> 0
    let inline isCompleted (s: int) : bool = (s &&& CompletedBit) <> 0

    let inline isAbort (s: int) : bool = (s &&& AbortBit) <> 0
    let inline isNotified (s: int) : bool = (s &&& NotifiedBit) <> 0
    let inline isAwaiterContext (s: int) : bool = (s &&& AwaiterContextBit) <> 0
    let inline isAwaiterExists (s: int) : bool = (s &&& AwaiterExistsBit) <> 0



[<Struct>]
[<RequireQualifiedAccess>]
type TransitIdleToRunningResult =
    /// Task successfully transit in running state. (Abort flag not set)
    | Ok
    /// Task successfully transit in running state, and abort flag set
    | OkAbortIsSet
    /// Task already in running state
    | ErrAlreadyRunning

[<Struct>]
[<RequireQualifiedAccess>]
type TransitRunningToIdleResult =
    /// End success, no other effects
    | Ok
    /// Task was notified until run
    | OkNotifyIsSet
    /// Task was aborted until was running
    | ErrAbortIsSet

[<Struct>]
[<RequireQualifiedAccess>]
type SetAwaiterContextResult =
    | Ok
    | ErrAlreadyCompleted

[<Struct>]
[<RequireQualifiedAccess>]
type UnsetAwaiterContextResult =
    | Ok
    | ErrAlreadyCompleted


[<Struct>]
[<RequireQualifiedAccess>]
type UnsetAwaiterExistsResult =
    | Ok
    | OkContextIsSet

[<Struct>]
[<RequireQualifiedAccess>]
type TransitRunningToCompletedResult =
    | OkAwaiterExists
    | OkAwaiterExistsWithContext
    | OkAwaiterNotExists

// [<Struct>]
// type WorkType =
//     | Poll
//     | Abort


/// <summary>
/// Implementation concurrent state for using in FutureTask implementations. <br></br>
///
/// <br></br>
///
/// This implementation designed for follow features:<br></br>
/// - Any work with spawned on runtime future being done in future runtime worker thread.
///   (Aborting also being done in future runtime worker thread.)<br></br>
/// - When spawned future waked until worker thread poll it, anyway future must be re-queued,<br></br>
///   to avoid starvation other futures<br></br>
/// - Abort being done as soon as possible (within above points):<br></br>
/// > * abort reschedule worker thread immediately<br></br>
/// > * if worker thread already do work, then after it is finished, future must abort immediately<br></br>
///
/// <br></br>
///
/// If your FutureTask implementation wants to be within other invariants,
/// you can implement custom FutureTask without this state implementation.<br></br>
///
/// <br></br>
/// ====***====
/// <br></br>
///
/// # Entities
/// - Worker thread - thread of runtime, that execute task
/// - Awaiter - Future that implement
///
///
/// # Awaiter context rules
///
/// The AwaiterContext field may be concurrently accessed by different threads:
/// in one thread the worker thread may complete a task and *read* the waker field to invoke the waker,
/// and in another thread the AwaiterFuture may be polled, and if the FutureTask hasn't yet completed,
/// the WorkerThread may *write* a future context to the AwaiterContext field.
/// The AwaiterContextBit bit ensures safe access by multiple threads
/// to the AwaiterContext field using the following rules:
///
/// 1. AwaiterContextBit initialized to zero
/// 2. AwaiterFuture has exclusive access to write AwaiterContextBit in Idle and Running states.
/// 3. WorkerThread has exclusive access to write AwaiterContextBit in Completed state.
/// 4. If AwaiterContextBit is zero and CompleteBit is zero, then
///    `Awaiter` can write to AwaiterContext field context value and set AwaiterContextBit to one
/// 5. If AwaiterContextBit is one  and CompleteBit is zero, then
///    `Worker` can set CompleteBit one and take AwaiterContext field and use Context to wake
/// 6. If CompleteBit is one independently AwaiterContextBit, then
///    Execution completed and nobody can do changes. But `Worker` can change AwaiterContext field to erase him
/// 7. If `Awaiter` gonna drop and set AwaiterExists bit to zero, then
///    `Awaiter` must set AwaiterContextBit to zero and clear AwaiterContext,
///    but ONLY IF CompleteBit is zero.
///
/// This rules guarantee one-time transfer Awaiter Context from Awaiter to Worker
/// without leek Context if Awaiter dropped. But waking of dropped Awaiter is possible.
/// But Context must support waking after termination related Future.
///
/// </summary>
[<Struct; NoComparison>]
type FutureTaskState =

    val mutable State: int

    new(initial: int) = { State = initial }

    /// Initial state for already notified (queued on runtime) FutureTask
    static member NewNotified = FutureTaskState(FutureTaskState.InitialNotified)
    static member NewNotifiedWithExistsAwaiter = FutureTaskState(FutureTaskState.InitialNotifiedWithAwaiterExists)
    static member New = FutureTaskState(0)

    member inline this.IsCompleted = FutureTaskState.isCompleted this.State
    member inline this.IsAbort = FutureTaskState.isAbort this.State

    // /// <summary>
    // /// Returns state preceding successful exchange
    // /// </summary>
    // /// <param name="f"> if -1, do not do exchange ops </param>
    // member inline this.FetchAndUpdate<'a>([<InlineIfLambda>] f: byref<'a> -> int -> int): 'a =
    //     let mutable doLoop = true
    //     let mutable state = this.State
    //     let mutable result = Unchecked.defaultof<'a>
    //     while doLoop do
    //         let newState = f &result state
    //         if newState = -1 then
    //             doLoop <- false
    //         else
    //             let state' = Interlocked.CompareExchange(&this.State, newState, state)
    //             if state' <> state then
    //                 state <- state'
    //             else
    //                 doLoop <- false
    //     result

    /// <summary>
    /// Transit to Running state.
    /// </summary>
    member inline this.TransitionIdleToRunning(): TransitIdleToRunningResult =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = Operators.Unchecked.defaultof<_>
        while doLoop do
            if not (FutureTaskState.isIdle state) then
                raise (InvalidOperationException("FutureTask is not in Idle state"))

            if FutureTaskState.isRunning state then
                result <- TransitIdleToRunningResult.ErrAlreadyRunning
                doLoop <- false
            else
                let newState = (state ||| FutureTaskState.RunningBit) &&& (~~~FutureTaskState.NotifiedBit)
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then state <- state'
                else
                    if FutureTaskState.isAbort state'
                    then result <- TransitIdleToRunningResult.OkAbortIsSet
                    else result <- TransitIdleToRunningResult.Ok
                    doLoop <- false
        result

    /// <summary>
    /// Transit future task from `Running` to `Idle` state.
    ///
    /// Fails if task flagged for abort.
    /// </summary>
    member inline this.TransitionRunningToIdle(): TransitRunningToIdleResult =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = Operators.Unchecked.defaultof<_>
        while doLoop do
            if not (FutureTaskState.isRunning state) then raise (InvalidOperationException())

            if FutureTaskState.isAbort state then
                // FutureTask aborted.
                result <- TransitRunningToIdleResult.ErrAbortIsSet
                doLoop <- false
            elif FutureTaskState.isNotified state then
                // FutureTask notifies until was running.
                // Reset running bit, not unset Notified bit,
                // user code must requeue task on scheduler to not let other task starvation
                let newState = state &&& (~~~FutureTaskState.RunningBit)
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then state <- state'
                else
                    result <- TransitRunningToIdleResult.OkNotifyIsSet
                    doLoop <- false
            else
                let newState = state &&& (~~~FutureTaskState.RunningBit)
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then state <- state'
                else
                    result <- TransitRunningToIdleResult.Ok
                    doLoop <- false
        result

    /// <summary>
    /// Transit future task from `Running` to `Idle` state.
    ///
    /// All possible resources must be used and cleared (awaiter context and result if awaiter not exists).
    /// </summary>
    member inline this.TransitionRunningToComplete() =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = Unchecked.defaultof<_>
        while doLoop do
            if not (FutureTaskState.isRunning state) then raise (InvalidOperationException())

            let newState = state ^^^ (FutureTaskState.RunningBit ||| FutureTaskState.CompletedBit)
            let state' = Interlocked.CompareExchange(&this.State, newState, state)
            if state' <> state then state <- state'
            else
                result <-
                    if FutureTaskState.isAwaiterExists state then
                        if FutureTaskState.isAwaiterContext state
                        then TransitRunningToCompletedResult.OkAwaiterExistsWithContext
                        else TransitRunningToCompletedResult.OkAwaiterExists
                    else
                        TransitRunningToCompletedResult.OkAwaiterNotExists
                doLoop <- false
        result

    /// <summary>
    ///
    /// </summary>
    /// <returns>
    /// true, if FutureTask must be queued for executing
    /// </returns>
    member inline this.SetNotify(): bool =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = false
        while doLoop do
            if FutureTaskState.isCompleted state || FutureTaskState.isNotified state then
                result <- false
                doLoop <- false
            else
                let newState = state ||| FutureTaskState.NotifiedBit
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then
                    state <- state'
                else
                    result <- FutureTaskState.isIdle state'
                    doLoop <- false
        result

    /// <summary>
    ///
    /// </summary>
    /// <returns>
    /// true, if FutureTask must be queued for executing
    /// </returns>
    member inline this.SetAbortAndNotify(): bool =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = false
        while doLoop do
            if FutureTaskState.isCompleted state || FutureTaskState.isAbort state then
                // Future task is terminated or already mark as abort. Aborting does not make a sense.
                // No-op optimization.
                result <- false
                doLoop <- false
            elif FutureTaskState.isRunning state then
                // FutureTask is processing.
                // Mark it active and abort bit set.
                // When runtime thread finish processing, it will check Abort and Processing bit
                // without repeated enqueue in scheduler queue.
                let newState = state ||| FutureTaskState.AbortBit ||| FutureTaskState.NotifiedBit
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then state <- state'
                else
                    result <- false
                    doLoop <- false
            else
                // FutureTask is idle.
                // Set active and abort bits.
                // Returns enqueue flag, if active bit is not set.
                let newState = state ||| FutureTaskState.AbortBit ||| FutureTaskState.NotifiedBit
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then state <- state'
                else
                    result <- not (FutureTaskState.isNotified state)
                    doLoop <- false
        result

    member inline this.SetAwaiterContext(): SetAwaiterContextResult =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = Unchecked.defaultof<_>
        while doLoop do
            if not (FutureTaskState.isAwaiterExists state) then
                raise (InvalidOperationException("FutureTask awaiter already dropped. Context can not be set."))
            if FutureTaskState.isAwaiterContext state then
                raise (InvalidOperationException("FutureTask awaiter context already set"))

            if FutureTaskState.isCompleted state then
                result <- SetAwaiterContextResult.ErrAlreadyCompleted
                doLoop <- false
            else
                let newState = state ||| FutureTaskState.AwaiterContextBit
                let state' = Interlocked.CompareExchange(&this.State, newState, state)
                if state' <> state then state <- state'
                else
                    result <- SetAwaiterContextResult.Ok
                    doLoop <- false
        result

    // member inline this.UnsetAwaiterContext(): UnsetAwaiterContextResult =
    //     let mutable doLoop = true
    //     let mutable state = this.State
    //     let mutable result = Unchecked.defaultof<_>
    //     while doLoop do
    //         if not (FutureTaskState.isAwaiterContext state) then
    //             raise (InvalidOperationException("FutureTask awaiter context already unset"))
    //
    //         if FutureTaskState.isCompleted state then
    //             result <- UnsetAwaiterContextResult.ErrAlreadyCompleted
    //             doLoop <- false
    //         else
    //             let newState = state &&& (~~~FutureTaskState.AwaiterContextBit)
    //             let state' = Interlocked.CompareExchange(&this.State, newState, state)
    //             if state' <> state then state <- state'
    //             else
    //                 result <- UnsetAwaiterContextResult.Ok
    //                 doLoop <- false
    //     result

    /// <summary>
    /// Unset AwaiterExistsBit and unset AwaiterContextBit
    /// </summary>
    member inline this.UnsetAwaiterExists(): UnsetAwaiterExistsResult =
        let mutable doLoop = true
        let mutable state = this.State
        let mutable result = Unchecked.defaultof<_>
        while doLoop do
            if not (FutureTaskState.isAwaiterExists state) then
                raise (InvalidOperationException("FutureTask AwaiterExistsBit already unset"))

            let hasContext = FutureTaskState.isAwaiterContext state

            let newState = state &&& (~~~ (FutureTaskState.AwaiterExistsBit ||| FutureTaskState.AwaiterContextBit))
            let state' = Interlocked.CompareExchange(&this.State, newState, state)
            if state' <> state then state <- state'
            else
                result <-
                    if hasContext
                    then UnsetAwaiterExistsResult.OkContextIsSet
                    else UnsetAwaiterExistsResult.Ok
                doLoop <- false
        result
