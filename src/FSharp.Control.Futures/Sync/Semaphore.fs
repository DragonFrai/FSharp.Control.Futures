namespace rec FSharp.Control.Futures.Sync

open System
open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


exception SemaphoreClosedException
exception SemaphorePermitsOverflowException


type internal SemaphoreAcquire =
    inherit IntrusiveNode<SemaphoreAcquire>

    val semaphore: Semaphore
    val permits: int
    val mutable queued: int // -1 if not queued
    val mutable primaryNotify: PrimaryNotify

    new(semaphore: Semaphore, permits: int) =
        { semaphore = semaphore
          permits = permits
          queued = -1
          primaryNotify = PrimaryNotify(false, false) }

    member inline this.IsQueued = this.queued <> -1
    member inline this.IsNotQueued = this.queued = -1

    interface IFuture<unit> with
        member this.Poll(ctx) =
            this.semaphore.PollAcquire(this, ctx)
        member this.Drop() =
            this.semaphore.DropAcquire(this)

type SemaphoreState' = int
[<Struct>]
type internal SemaphoreState =
    // Contains non-negative permits count or -1 for closed state

    static member inline New(permits: int): int =
        if permits < 0 then
            invalidArg (nameof(permits)) "InitialPermits can't be negative."
        permits

    static member inline NewClosed(): int =
        -1

    static member inline Permits(state: int): int =
        state

    static member inline Close(_state: int): int =
        -1

    static member inline IsClosed(state: int): bool =
        state = -1

    static member inline AssertNotClosed(state: int): unit =
        if SemaphoreState.IsClosed(state) then raise SemaphoreClosedException

    static member inline AddPermits(state: int, permits: int): int =
        let state = state + permits
        if state < 0 then raise SemaphorePermitsOverflowException
        else state

    static member inline SubPermits(state: int, permits: int): int =
        let state = state - permits
        if state < 0 then raise SemaphorePermitsOverflowException
        else state

    static member inline CompareExchange(stateRef: int byref, newState: int, comparandState: int): int =
        Interlocked.CompareExchange(&stateRef, newState, comparandState)


// TODO?: Поддержка разных режимов порядка Fifi/Lifo/Drain
// [<Struct>]
// type SemaphoreMode =
//     | Fifo
//     | Lifo
//     | Drain

// TODO: Гарантировать порядок Fifo.
//       Сейчас если пришел мелкий запрос на ресурсы после большого поставленного в очередь,
//       мелкий будет удовлетворен независимо от наличия большего в очереди.
//       Альтернатива: Вообще не думать о гарантировании порядка.
//       Примечание: Запрос считается пришедшим после вызова Poll AcquireFuture, а не после вызова метода Acquire семафора.

/// <summary>
/// Async Semaphore implementation.
/// </summary>
type Semaphore =

    val mutable internal state: int // Semaphore state
    val internal syncObj: obj
    val mutable internal acquiresQueue: IntrusiveList<SemaphoreAcquire>

    static member inline MaxPermits: int = Int32.MaxValue

    private new(state: int, syncObj: obj, acquireQueue: IntrusiveList<SemaphoreAcquire>) =
        { state = state
          syncObj = syncObj
          acquiresQueue = acquireQueue }

    new(initialPermits: int) =
        Semaphore(SemaphoreState.New(initialPermits), obj(), IntrusiveList.Create())

    new() =
        Semaphore(0)

    /// Create closed semaphore
    static member Closed(): Semaphore =
        Semaphore(SemaphoreState.NewClosed(), obj(), IntrusiveList.Create())

    // <Internal>

    member internal this.PollAcquire(acquire: SemaphoreAcquire, ctx: IContext): Poll<unit> =
        lock this.syncObj ^fun () ->
            if acquire.IsNotQueued then
                let state = this.state
                SemaphoreState.AssertNotClosed(state)
                if SemaphoreState.Permits(state) >= acquire.permits then
                    this.state <- SemaphoreState.AddPermits(state, -acquire.permits)
                    acquire.queued <- acquire.permits
                    acquire.primaryNotify.Notify() |> ignore
                else
                    acquire.queued <- acquire.permits
                    this.acquiresQueue.PushBack(acquire)
            else
                if not acquire.primaryNotify.IsNotified then
                    let state = this.state
                    SemaphoreState.AssertNotClosed(state)
                    if SemaphoreState.Permits(state) >= acquire.permits then
                        this.state <- SemaphoreState.AddPermits(state, -acquire.permits)
                        acquire.primaryNotify.Notify() |> ignore
                    else
                        ()

        if acquire.primaryNotify.Poll(ctx)
        then
            let state = this.state
            if SemaphoreState.IsClosed(state)
            then raise SemaphoreClosedException
            else Poll.Ready ()
        else Poll.Pending

    member internal this.ReleasePermitsNoLock(permits: int): unit =
        let mutable state = this.state
        SemaphoreState.AssertNotClosed(state)
        state <- SemaphoreState.AddPermits(state, permits)
        while (isNotNull this.acquiresQueue.startNode) && SemaphoreState.Permits(state) >= this.acquiresQueue.startNode.permits do
            let next = this.acquiresQueue.PopFront()
            state <- SemaphoreState.SubPermits(state, next.permits)
            do next.primaryNotify.Notify() |> ignore
        this.state <- state

    member internal this.DropAcquire(acquire: SemaphoreAcquire): unit =
        if not (acquire.queued >= 0) then
            ()
        else
            lock this.syncObj ^fun () ->
                let wasInQueue = this.acquiresQueue.Remove(acquire)
                if wasInQueue then
                    // Acquire future wait permits. Adding permits not required
                    ()
                else
                    this.ReleasePermitsNoLock(acquire.permits)

    member internal this.ReleasePermits(permits: int) =
        if permits = 0 then ()
        else
            lock this.syncObj ^fun () ->
                this.ReleasePermitsNoLock(permits)

    // </Internal>

    member this.AvailablePermits: int =
        let state = this.state
        SemaphoreState.AssertNotClosed(state)
        SemaphoreState.Permits(state)

    member this.TryAcquire(permits: int): bool =
        if permits = 0 then true
        else
        lock this.syncObj ^fun () ->
            let state = this.state
            SemaphoreState.AssertNotClosed(state)
            if SemaphoreState.Permits(state) < permits then
                false
            else
                this.state <- SemaphoreState.SubPermits(state, permits)
                true

    member this.TryAcquire(): bool =
        this.TryAcquire(1)

    /// <summary>
    /// Decrease a semaphore permits by maximum of `permits`.
    /// If it’s not possible to reduce by `permits`,
    /// reduce the number of permits to 0 and returns their number.
    /// </summary>
    /// <param name="permits"> Maximum of decreased permits </param>
    /// <returns> Number of permits that were actually reduced </returns>
    member this.AcquireUp(permits: int): int =
        failwith "TODO"

    member this.Acquire(permits: int): Future<unit> =
        Trace.Assert(permits <= Semaphore.MaxPermits, "MaxPermits has been exceeded")
        SemaphoreAcquire(this, permits)

    member this.Acquire(): Future<unit> =
        this.Acquire(1)

    member this.Release(permits: int): unit =
        this.ReleasePermits(permits)

    member this.Release(): unit =
        this.Release(1)

    /// <summary>
    /// Increase semaphore permits,
    /// So that total permits did not exceed limit permits.
    ///
    /// </summary>
    /// <param name="permits"> Maximum of increased permits </param>
    /// <param name="limit"> Maximum of total permits </param>
    /// <returns> Number of permits that were actually increased </returns>
    member this.ReleaseUp(permits: int, limit: int): int =
        failwith "TODO"

    member this.Close(): unit =
        if SemaphoreState.IsClosed(this.state) then ()
        else
            let acquireQueue =
                lock this.syncObj ^fun () ->
                    this.state <- SemaphoreState.Close(this.state)
                    this.acquiresQueue.Drain()
            acquireQueue |>
            IntrusiveNode.forEach (fun acquire -> acquire.primaryNotify.Notify() |> ignore)


module Semaphore =
    let inline create (initialPermits: int) : Semaphore = Semaphore(initialPermits)
    let inline availablePermits (semaphore: Semaphore) : int = semaphore.AvailablePermits
    let inline acquire (semaphore: Semaphore) : Future<unit> = semaphore.Acquire()
    let inline acquireMany (permits: int) (semaphore: Semaphore) : Future<unit> = semaphore.Acquire(permits)
    let inline acquireUp (permits: int) (semaphore: Semaphore) : int = semaphore.AcquireUp(permits)
    let inline tryAcquire (semaphore: Semaphore) : bool = semaphore.TryAcquire()
    let inline tryAcquireMany (permits: int) (semaphore: Semaphore) : bool = semaphore.TryAcquire(permits)
    let inline release (semaphore: Semaphore) : unit = semaphore.Release()
    let inline releaseMany (permits: int) (semaphore: Semaphore) : unit = semaphore.Release(permits)
    let inline close (semaphore: Semaphore) : unit = semaphore.Close()
