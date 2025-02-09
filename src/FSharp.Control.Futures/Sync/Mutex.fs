namespace FSharp.Control.Futures.Sync

open System
open FSharp.Control.Futures


[<Sealed>]
type Mutex =
    val private semaphore: Semaphore

    new() =
        { semaphore = Semaphore(1) }

    member this.Lock(): Future<unit> =
        this.semaphore.Acquire()

    member this.TryLock(): bool =
        this.semaphore.TryAcquire()

    member this.LockBlocking(): unit =
        this.Lock() |> Future.runBlocking

    member this.Unlock(): unit =
        // TODO: Not thread safe checking invalid mutex usage
        if this.semaphore.AvailablePermits > 0 then invalidOp "Unlocking not locked mutex"
        this.semaphore.Release() // TODO: Implement ReleaseUp in Semaphore


[<RequireQualifiedAccess>]
module Mutex =
    let inline create () =
        Mutex()

    let inline lock (mutex: Mutex) : Future<unit> =
        mutex.Lock()

    let inline tryLock (mutex: Mutex) : bool =
        mutex.TryLock()

    let inline lockBlocking (mutex: Mutex) : unit =
        mutex.LockBlocking()

    let inline unlock (mutex: Mutex) : unit =
        mutex.Unlock()
