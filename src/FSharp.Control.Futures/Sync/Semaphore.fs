namespace rec FSharp.Control.Futures.Sync

open System
open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
// open Microsoft.FSharp.Core


exception SemaphoreClosedException
exception SemaphorePermitsOverflowException


type internal SemaphoreAcquire =
    inherit IntrusiveNode<SemaphoreAcquire>

    val semaphore: Semaphore
    val permits: int
    val mutable queued: bool
    val mutable primaryNotify: PrimaryNotify

    new(semaphore: Semaphore, permits: int) =
        { semaphore = semaphore
          permits = permits
          queued = false
          primaryNotify = PrimaryNotify(false) }

    interface IFuture<unit> with
        member this.Poll(ctx) =
            this.semaphore.PollAcquire(this, ctx)
        member this.Drop() =
            this.semaphore.DropAcquire(this)



[<Struct>]
type internal SemaphoreState =
    // Contains non-negative permits count or -1 for closed state
    val state: int
    new(state: int) = { state = state }

    static member inline WithPermits(permits: int): SemaphoreState =
        if permits < 0 then raise SemaphorePermitsOverflowException
        SemaphoreState(permits)

    member inline this.AsInt: int =
        this.state

    member inline this.IsClosed: bool =
        this.state = -1

    member inline this.AssertNotClosed(): unit =
        if this.IsClosed then raise SemaphoreClosedException

    member inline this.AssertNotClosedPipe(): SemaphoreState =
        this.AssertNotClosed()
        this

    member inline this.Permits: int =
        this.state

    member inline this.AddPermits(permits: int): SemaphoreState =
        let permits = this.state + permits
        if permits < 0 then raise SemaphorePermitsOverflowException
        else SemaphoreState(permits)

    member inline this.Close(): SemaphoreState =
        SemaphoreState(-1)


type Semaphore =

    val mutable internal state: int // Semaphore state
    val internal syncObj: obj
    val mutable internal acquiresQueue: IntrusiveList<SemaphoreAcquire>

    static member inline MaxPermits: int = Int32.MaxValue

    new(initialPermits: int) =
        Trace.Assert(initialPermits <= Semaphore.MaxPermits, "MaxPermits has been exceeded")
        { syncObj = obj ()
          state = SemaphoreState.WithPermits(initialPermits).AsInt
          acquiresQueue = IntrusiveList.Create() }

    // <Internal>

    member internal this.PollAcquire(acquire: SemaphoreAcquire, ctx: IContext): Poll<unit> =
        lock this.syncObj ^fun () ->
            if not acquire.queued then
                let state = SemaphoreState(this.state)
                state.AssertNotClosed()
                if state.Permits >= acquire.permits then
                    this.state <- state.AddPermits(-acquire.permits).AsInt
                    acquire.queued <- true
                    acquire.primaryNotify.Notify() |> ignore
                else
                    acquire.queued <- true
                    this.acquiresQueue.PushBack(acquire)

        if acquire.primaryNotify.Poll(ctx)
        then
            let state = SemaphoreState(this.state)
            if state.IsClosed
            then raise SemaphoreClosedException
            else Poll.Ready ()
        else Poll.Pending

    member internal this.DropAcquire(acquire: SemaphoreAcquire): unit =
        if not acquire.queued then
            ()
        else
            lock this.syncObj ^fun () ->
                // Acquire future wait permits. Adding permits not required
                this.acquiresQueue.Remove(acquire) |> ignore

    member internal this.ReleasePermits(permits: int) =
        if permits = 0 then ()
        else
            lock this.syncObj ^fun () ->
                this.state <- SemaphoreState(this.state).AssertNotClosedPipe().AddPermits(permits).AsInt

                while (isNotNull this.acquiresQueue.startNode) && this.state >= this.acquiresQueue.startNode.permits do
                    let next = this.acquiresQueue.PopFront()
                    this.state <- this.state - next.permits
                    next.primaryNotify.Notify()

    // </Internal>

    member this.AvailablePermits: int = this.state

    member this.TryAcquire(permits: int): bool =
        if permits = 0 then true
        else
        lock this.syncObj ^fun () ->
            let state = SemaphoreState(this.state)
            state.AssertNotClosed()
            if state.Permits < permits then
                false
            else
                this.state <- state.AddPermits(-permits).AsInt
                true

    member this.TryAcquire(): bool =
        this.TryAcquire(1)

    member this.Acquire(permits: int): Future<unit> =
        Trace.Assert(permits <= Semaphore.MaxPermits, "MaxPermits has been exceeded")
        SemaphoreAcquire(this, permits)

    member this.Acquire(): Future<unit> =
        this.Acquire(1)

    member this.Release(permits: int): unit =
        this.ReleasePermits(permits)

    member this.Release(): unit =
        this.Release(1)

    member this.Close(): unit =
        if SemaphoreState(this.state).IsClosed then ()
        else
            let acquireQueue =
                lock this.syncObj ^fun () ->
                    this.state <- SemaphoreState(this.state).Close().AsInt
                    this.acquiresQueue.Drain()
            acquireQueue |>
            IntrusiveNode.forEach (fun acquire -> acquire.primaryNotify.Notify() |> ignore)


module Semaphore =
    ()
