namespace rec FSharp.Control.Futures.Sync

open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


[<AutoOpen>]
module private SemaphoreLiterals =
    // 8 bits reserved for implementation state
    let [<Literal>] MaxPermits = 0x00FFFFFF

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
            this.semaphore.PollAcquired(this, ctx)
        member this.Drop() =
            this.semaphore.DropAcquired(this)

type Semaphore =

    val syncObj: obj
    val mutable internal permits: int
    val mutable internal acquiresQueue: IntrusiveList<SemaphoreAcquire>

    new(initialPermits: int) =
        Trace.Assert(initialPermits <= MaxPermits, "MaxPermits has been exceeded")
        { syncObj = obj ()
          permits = initialPermits
          acquiresQueue = IntrusiveList.Create() }

    member internal this.PollAcquired(acquire: SemaphoreAcquire, ctx: IContext): Poll<unit> =
        lock this.syncObj ^fun () ->
            if not acquire.queued then
                if this.permits >= acquire.permits then
                    this.permits <- this.permits - acquire.permits
                    acquire.queued <- true
                    acquire.primaryNotify.Notify() |> ignore
                else
                    acquire.queued <- true
                    this.acquiresQueue.PushBack(acquire)

        if acquire.primaryNotify.Poll(ctx)
        then Poll.Ready ()
        else Poll.Pending

    member internal this.DropAcquired(acquire: SemaphoreAcquire): unit =
        if not acquire.queued then
            ()
        else
            lock this.syncObj ^fun () ->
                this.acquiresQueue.Remove(acquire) |> ignore

    member internal this.ReleasePermits(permits: int) =
        if permits = 0 then ()
        else
            lock this.syncObj ^fun () ->
                this.permits <- this.permits + permits

                while (isNotNull this.acquiresQueue.startNode) && this.permits >= this.acquiresQueue.startNode.permits do
                    let next = this.acquiresQueue.PopFront()
                    this.permits <- this.permits - next.permits
                    next.primaryNotify.Notify()

    member this.Acquire(permits: int): Future<unit> =
        SemaphoreAcquire(this, permits)

    member this.Acquire(): Future<unit> =
        this.Acquire(1)

    member this.Release(permits: int): unit =
        this.ReleasePermits(permits)

    member this.Release(): unit =
        this.Release(1)
