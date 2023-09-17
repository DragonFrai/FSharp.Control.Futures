namespace rec FSharp.Control.Futures.Lock

open FSharp.Control.Futures
open FSharp.Control.Futures.Internals


// =======
// classes

exception MutexGuardMultipleUnlockException
exception MutexAlreadyUnlockedException

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
            if isNull this.mutex then raise MutexAlreadyUnlockedException
            this.mutex.value
        and set (value) =
            if isNull this.mutex then raise MutexAlreadyUnlockedException
            this.mutex.value <- value

    member inline this.SetValue(value): unit =
        this.Value <- value

    member this.Mutex: Mutex<'a> =
        if isNull this.mutex then raise MutexGuardMultipleUnlockException
        this.mutex

    member this.Unlock() : unit =
        this.mutex.UnlockUnchecked()
        this.mutex <- nullObj

// classes
// =======
// modules

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

    let inline unlock (guard: MutexGuard<'a>) : unit =
        MutexGuard.unlock guard

    // let read (f: 'a -> 'b) (mutex: Mutex<'a>) : Future<'b> = future {
    //     let! guard = mutex.Lock()
    //     let! b = f guard.Value
    //     guard.Unlock()
    //     return b
    // }
    //
    // let write (replacement: 'a) (mutex: Mutex<'a>) : Future<unit> = future {
    //     let! guard = mutex.Lock()
    //     guard.SetValue(replacement)
    //     guard.Unlock()
    // }

    // TODO: Add try in all update*
    let update (f: 'a -> Future<'a>) (mutex: Mutex<'a>) : Future<unit> = future {
        let! guard = mutex.Lock()
        let! a = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        ()
    }

    let updateR (f: 'a -> Future<'a * 'r>) (mutex: Mutex<'a>) : Future<'r> = future {
        let! guard = mutex.Lock()
        let! (a, r) = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        return r
    }

    let updateSync (f: 'a -> 'a) (mutex: Mutex<'a>) : Future<unit> = future {
        let! guard = mutex.Lock()
        let a = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        ()
    }

    let updateSyncR (f: 'a -> 'a * 'r) (mutex: Mutex<'a>) : Future<'r> = future {
        let! guard = mutex.Lock()
        let (a, r) = f guard.Value
        guard.SetValue(a)
        guard.Unlock()
        return r
    }

// modules
// =======
