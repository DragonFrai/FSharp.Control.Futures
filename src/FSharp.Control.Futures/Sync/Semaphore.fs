namespace rec FSharp.Control.Futures.Sync

open System
open System.Diagnostics
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


// TODO: Fix new closing without drop old available permits

exception SemaphoreClosedException
exception SemaphorePermitsOverflowException


[<Struct>]
[<RequireQualifiedAccess>]
type AcquireResult =
    | Ok
    | Closed
    | NoPermits
    with
        member this.Unwrap(): unit =
            match this with
            | Ok -> ()
            | Closed -> raise SemaphoreClosedException
            | NoPermits -> raise SemaphorePermitsOverflowException

        member this.IsError: bool =
            match this with
            | Ok -> false
            | _ -> true


type internal AcquireState =

    static member inline IsQueued(acquire: SemaphoreAcquire) : bool =
        acquire.state <> -1

    static member inline IsNotQueued(acquire: SemaphoreAcquire) : bool =
        acquire.state = -1


type internal SemaphoreAcquire =
    inherit IntrusiveNode<SemaphoreAcquire>

    val semaphore: Semaphore
    val acquiredPermits: int
    /// <summary>
    /// Atomic state, that
    /// contains remaining permits or -1 if SemaphoreAcquire not queued
    /// </summary>
    val mutable state: int
    val mutable primaryNotify: PrimaryNotify

    member inline this.UnsatisfiedPermits = this.acquiredPermits - this.state

    new(semaphore: Semaphore, permits: int) =
        { semaphore = semaphore
          acquiredPermits = permits
          state = -1
          primaryNotify = PrimaryNotify(false, false) }

    interface IFuture<unit> with
        member this.Poll(ctx) =
            match this.semaphore.PollAcquire(this, ctx) with
            | NaivePoll.Ready _result -> Poll.Ready ()
            | NaivePoll.Pending -> Poll.Pending

        member this.Drop() =
            this.semaphore.DropAcquire(this)

    interface IFuture<bool> with
        member this.Poll(ctx) =
            this.semaphore.PollAcquire(this, ctx) |> NaivePoll.toPoll
        member this.Drop() =
            this.semaphore.DropAcquire(this)

/// Contains non-negative permits count or -1 for closed state
type internal SemaphoreState =

    [<Literal>]
    static let CLOSE_BIT_MASK = 0x8000_0000

    [<Literal>]
    static let PERMITS_MASK = 0x7FFF_FFFF

    static member inline New(permits: int): int =
        if permits < 0 then
            invalidArg (nameof(permits)) "Initial permits can't be negative."
        permits

    static member inline NewClosed(permits: int): int =
        if permits < 0 then
            invalidArg (nameof(permits)) "Initial permits can't be negative."
        CLOSE_BIT_MASK ||| permits

    static member inline Permits(state: int): int =
        state &&& PERMITS_MASK

    static member inline Close(state: int): int =
        state ||| CLOSE_BIT_MASK

    /// Set permits to 0
    static member inline DrainPermits(state: int): int =
        (state &&& CLOSE_BIT_MASK)

    static member inline SetPermitsUnchecked(state: int, permits: int): int =
        (state &&& CLOSE_BIT_MASK) ||| permits

    static member inline SetPermits(state: int, permits: int): int =
        if permits < 0 then raise SemaphorePermitsOverflowException
        else (state &&& CLOSE_BIT_MASK) ||| permits

    static member inline IsClosed(state: int): bool =
        (state &&& CLOSE_BIT_MASK) <> 0

    static member inline AssertNotClosed(state: int): unit =
        if SemaphoreState.IsClosed(state) then raise SemaphoreClosedException

    static member inline AddPermits(state: int, permits: int): int =
        let permits = SemaphoreState.Permits(state) + permits
        SemaphoreState.SetPermits(state, permits)

    static member inline SubPermits(state: int, permits: int): int =
        let permits = state - SemaphoreState.Permits(permits)
        SemaphoreState.SetPermits(state, permits)

    static member inline SubPermitsUnchecked(state: int, permits: int): int =
        (state &&& CLOSE_BIT_MASK) ||| (SemaphoreState.Permits(state) - permits)

    static member inline TryAcquire(state: int, permits: int, newState: outref<int>): AcquireResult =
        let availablePermits = SemaphoreState.Permits(state)
        let closedBit = state &&& CLOSE_BIT_MASK
        if availablePermits >= permits then
            newState <- closedBit ||| (availablePermits - permits)
            AcquireResult.Ok
        elif closedBit <> 0 then
            newState <- closedBit
            AcquireResult.Closed
        else
            newState <- closedBit
            AcquireResult.NoPermits


    static member inline AcquireOrDrain(state: int, permits: int, newState: outref<int>, usedPermits: outref<int>): AcquireResult =
        let availablePermits = SemaphoreState.Permits(state)
        let closedBit = state &&& CLOSE_BIT_MASK
        if availablePermits >= permits then
            newState <- closedBit ||| (availablePermits - permits)
            usedPermits <- permits
            AcquireResult.Ok
        elif closedBit <> 0 then
            newState <- closedBit
            usedPermits <- availablePermits
            AcquireResult.Closed
        else
            newState <- closedBit
            usedPermits <- availablePermits
            AcquireResult.NoPermits


// TODO?: Поддержка разных режимов порядка Fifi/Lifo/Drain
// [<Struct>]
// type SemaphoreMode =
//     | Fifo
//     | Lifo
//     | Drain

/// <summary>
/// Async Semaphore implementation.
/// </summary>
[<Sealed>]
type Semaphore =

    val mutable internal state: int // Semaphore state
    val internal queueLock: obj
    // Accessing require lock on syncObj
    val mutable internal acquiresQueue: IntrusiveList<SemaphoreAcquire>

    static member inline MaxPermits: int = Int32.MaxValue

    private new(state: int, syncObj: obj, acquireQueue: IntrusiveList<SemaphoreAcquire>) =
        { state = state
          queueLock = syncObj
          acquiresQueue = acquireQueue }

    new(initialPermits: int) =
        Semaphore(SemaphoreState.New(initialPermits), obj(), IntrusiveList.Create())

    new() =
        Semaphore(0)

    /// Create closed semaphore
    static member Closed(permits: int): Semaphore =
        Semaphore(SemaphoreState.NewClosed(permits), obj(), IntrusiveList.Create())

    static member Closed(): Semaphore =
        Semaphore(SemaphoreState.NewClosed(0), obj(), IntrusiveList.Create())

    // <Internal>

    member internal this.PollAcquire(acquire: SemaphoreAcquire, ctx: IContext): NaivePoll<bool> =
        let mutable acquire = acquire
        lock this.queueLock ^fun () ->
            if AcquireState.IsNotQueued(acquire) then
                let semaphoreState = this.state
                let mutable semaphoreState' = 0
                let mutable usedPermits = 0
                let res = SemaphoreState.AcquireOrDrain(semaphoreState, acquire.acquiredPermits, &semaphoreState', &usedPermits)
                match res with
                | AcquireResult.Ok ->
                    this.state <- semaphoreState'
                    acquire.state <- acquire.acquiredPermits
                    acquire.primaryNotify.Notify() |> ignore
                | AcquireResult.Closed ->
                    // permits должны быть исчерпаны, чтобы следующие ожидающие не могли их использовать.
                    // Это сохранит последовательность взятия в семафоре.
                    this.state <- semaphoreState'
                    acquire.state <- usedPermits
                | AcquireResult.NoPermits ->
                    this.state <- semaphoreState'
                    acquire.state <- usedPermits
                    this.acquiresQueue.PushBack(acquire)
            else
                // Already queued. Wait Notify. (permits count updated while Releasing)
                ()

        if acquire.primaryNotify.Poll(ctx)
        then
            let state = this.state
            // TODO: Determine fact of closing using notification property
            if SemaphoreState.IsClosed(state)
            then NaivePoll.Ready false
            else NaivePoll.Ready true
        else NaivePoll.Pending

    member internal this.ReleasePermitsNoLock(permits: int): unit =
        let mutable state = this.state
        SemaphoreState.AssertNotClosed(state)
        state <- SemaphoreState.AddPermits(state, permits)
        let mutable doLoop = true
        while doLoop && (isNotNull this.acquiresQueue.startNode) do
            let availablePermits = SemaphoreState.Permits(state)
            let nextAcquire = this.acquiresQueue.startNode
            if availablePermits >= nextAcquire.UnsatisfiedPermits then
                let next = this.acquiresQueue.PopFront()
                state <- SemaphoreState.SubPermitsUnchecked(state, next.UnsatisfiedPermits)
                do next.primaryNotify.Notify() |> ignore
            else
                doLoop <- false
        this.state <- state

    member internal this.DropAcquire(acquire: SemaphoreAcquire): unit =
        if not (acquire.state >= 0) then
            ()
        else
            lock this.queueLock ^fun () ->
                let wasInQueue = this.acquiresQueue.Remove(acquire)
                if wasInQueue then
                    // Acquire future wait permits. Adding permits not required
                    ()
                else
                    this.ReleasePermitsNoLock(acquire.acquiredPermits)

    member internal this.ReleasePermits(permits: int) =
        if permits = 0 then ()
        else
            lock this.queueLock ^fun () ->
                this.ReleasePermitsNoLock(permits)

    // </Internal>

    member this.AvailablePermits: int =
        let state = this.state
        SemaphoreState.AssertNotClosed(state)
        SemaphoreState.Permits(state)

    member this.AcquireNow(permits: int): AcquireResult =
        if permits = 0 then AcquireResult.Ok
        else
        lock this.queueLock ^fun () ->
            let state = this.state
            let mutable state' = 0
            let result = SemaphoreState.TryAcquire(state, permits, &state')
            if result = AcquireResult.Ok then
                this.state <- state'
            result

    member this.AcquireNow(): AcquireResult =
        this.AcquireNow(1)

    member this.Acquire(permits: int): Future<unit> =
        Trace.Assert(permits <= Semaphore.MaxPermits, "MaxPermits has been exceeded")
        SemaphoreAcquire(this, permits)

    member this.Acquire(): Future<unit> =
        this.Acquire(1)

    member this.TryAcquire(permits: int): Future<bool> =
        Trace.Assert(permits <= Semaphore.MaxPermits, "MaxPermits has been exceeded")
        SemaphoreAcquire(this, permits)

    member this.TryAcquire(): Future<bool> =
        this.TryAcquire(1)

    member this.Release(permits: int): unit =
        this.ReleasePermits(permits)

    member this.Release(): unit =
        this.Release(1)

    member this.Close(): unit =
        if SemaphoreState.IsClosed(this.state) then ()
        else
            let acquireQueue =
                lock this.queueLock ^fun () ->
                    this.state <- SemaphoreState.Close(this.state)
                    this.acquiresQueue.Drain()
            acquireQueue |>
            IntrusiveNode.forEach (fun acquire -> acquire.primaryNotify.Notify() |> ignore)

    member this.IsClosed: bool =
        SemaphoreState.IsClosed(this.state)

module Semaphore =
    let inline create (initialPermits: int) : Semaphore = Semaphore(initialPermits)
    let inline availablePermits (semaphore: Semaphore) : int = semaphore.AvailablePermits
    let inline acquire (semaphore: Semaphore) : Future<unit> = semaphore.Acquire()
    let inline acquireMany (permits: int) (semaphore: Semaphore) : Future<unit> = semaphore.Acquire(permits)
    let inline tryAcquire (semaphore: Semaphore) : AcquireResult = semaphore.AcquireNow()
    let inline tryAcquireMany (permits: int) (semaphore: Semaphore) : AcquireResult = semaphore.AcquireNow(permits)
    let inline release (semaphore: Semaphore) : unit = semaphore.Release()
    let inline releaseMany (permits: int) (semaphore: Semaphore) : unit = semaphore.Release(permits)
    let inline close (semaphore: Semaphore) : unit = semaphore.Close()
