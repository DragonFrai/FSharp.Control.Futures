namespace rec FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.Internals


exception MutexGuardMultipleUnlockException

[<Sealed>]
type Mutex<'a> =
    // main state
    val mutable internal value: 'a
    val monitor: Monitor

    member this.TryLock() : Result<MutexGuard<'a>, unit> =
        match this.monitor.TryLock() with
        | true -> Ok (MutexGuard(this))
        | false -> Error ()

    member this.Lock() : Future<MutexGuard<'a>> =
        this.monitor.Lock()
        |> Future.map (fun () -> MutexGuard(this))

    member this.BlockingLock() : MutexGuard<'a> =
        do this.monitor.BlockingLock()
        MutexGuard(this)

    member this.UnlockUnchecked() : unit =
        this.monitor.Unlock()

    new(init: 'a) =
        { value = init
          monitor = Monitor() }

[<Struct; NoEquality; NoComparison>]
type MutexGuard<'a> =
    val mutable private mutex: Mutex<'a>
    internal new (mutex: Mutex<'a>) = { mutex = mutex }

    member this.Value
        with get () =
            if isNull this.mutex then raise MutexGuardMultipleUnlockException
            this.mutex.value
        and set (value) =
            if isNull this.mutex then raise MutexGuardMultipleUnlockException
            this.mutex.value <- value

    member inline this.SetValue(value): unit =
        this.Value <- value

    member this.Mutex: Mutex<'a> =
        if isNull this.mutex then raise MutexGuardMultipleUnlockException
        this.mutex

    member this.Unlock() : unit =
        this.mutex.UnlockUnchecked()
        this.mutex <- nullObj

module MutexGuard =
    let inline unlock (guard: MutexGuard<'a>) : unit =
        guard.Unlock()

module Mutex =
    let inline create (init: 'a) : Mutex<'a> =
        Mutex(init)

    let inline lock (mutex: Mutex<'a>) : Future<MutexGuard<'a>> =
        mutex.Lock()

    let inline tryLock (mutex: Mutex<'a>) : Result<MutexGuard<'a>, unit> =
        mutex.TryLock()

    let inline blockingLock (mutex: Mutex<'a>) : MutexGuard<'a> =
        mutex.BlockingLock()

    let lockMR (f: 'a -> Future<'a * 'r>) (mutex: Mutex<'a>) : Future<'r> = future {
        let! guard = mutex.Lock()
        let! (a, r) = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        return r
    }

    let lockM (f: 'a -> Future<'a>) (mutex: Mutex<'a>) : Future<unit> = future {
        let! guard = mutex.Lock()
        let! a = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        ()
    }

    let lockS (f: 'a -> 'a) (mutex: Mutex<'a>) : Future<unit> = future {
        let! guard = mutex.Lock()
        let a = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        ()
    }
