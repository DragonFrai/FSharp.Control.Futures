namespace rec FSharp.Control.Futures.Sync

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Internals
open FSharp.Control.Futures


exception MonitorAlreadyUnlockedException

/// Высокоуровневый низкоуровневый примитив асинхронной синхронизации, аналогичный обычному мьютексу
[<Sealed>]
type Monitor =
    // sync
    val internal _sync: SpinLock

    // synced state
    val internal _lockers: IntrusiveList<MonitorWaiter>
    val mutable internal _isLocked: bool

    new() =
        { _sync = SpinLock(false)
          _lockers = IntrusiveList.Create()
          _isLocked = false }

    member this.TryLock() : bool =
        let mutable hasLock = false
        this._sync.Enter(&hasLock)
        if this._isLocked then
            if hasLock then this._sync.Exit()
            false
        else
            this._isLocked <- true
            if hasLock then this._sync.Exit()
            true

    member this.Lock() : Future<unit> = MonitorWaiter(this)

    member this.BlockingLock() : unit = this.Lock() |> Future.runBlocking

    member this.Unlock() : unit =
        let mutable hasLock = false
        this._sync.Enter(&hasLock)
        if this._isLocked then
            this._isLocked <- false
            let lockers = this._lockers.Drain()
            lockers |> IntrusiveNode.forEach (fun x -> if isNotNull x._context then x._context.Wake())
            if hasLock then this._sync.Exit()
            ()
        else
            if hasLock then this._sync.Exit()
            raise MonitorAlreadyUnlockedException

[<Sealed>]
type MonitorWaiter =
    inherit IntrusiveNode<MonitorWaiter>

    // not null only when mutex.lockers contains this
    val mutable internal _context: IContext
    val mutable internal _monitor: Monitor

    internal new (mutex: Monitor) = { inherit IntrusiveNode<MonitorWaiter>(); _monitor = mutex; _context = nullObj }

    interface Future<unit> with
        member this.Poll(ctx) =
            if isNull this._monitor then raise (FutureTerminatedException())
            let monitor = this._monitor
            let mutable hasLock = false
            monitor._sync.Enter(&hasLock)
            if monitor._isLocked then
                if isNull this._context then
                    this._context <- ctx
                    monitor._lockers.PushBack(this)
                if hasLock then monitor._sync.Exit()
                Poll.Pending
            else
                if isNotNull this._context then
                    monitor._lockers.Remove(this) |> ignore
                    this._context <- nullObj
                    this._monitor <- nullObj
                monitor._isLocked <- true
                if hasLock then monitor._sync.Exit()
                this._monitor <- nullObj
                Poll.Ready ()

        member this.Drop() =
            if isNull this._monitor then raise (FutureTerminatedException())
            let monitor = this._monitor
            let mutable hasLock = false
            monitor._sync.Enter(&hasLock)
            if isNotNull this._context then
                monitor._lockers.Remove(this) |> ignore
                this._context <- nullObj
                this._monitor <- nullObj
            if hasLock then monitor._sync.Exit()

