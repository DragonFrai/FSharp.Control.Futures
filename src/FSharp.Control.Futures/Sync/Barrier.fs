namespace FSharp.Control.Futures.Sync

open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync
open Microsoft.FSharp.Core


/// <summary>
/// </summary>
type Barrier =

    val private barrierLimit: int
    val mutable private barrierLeft: int
    val private semaphore: Semaphore

    new(barrierLimit: int) =
        if barrierLimit < 0 then
            invalidArg (nameof barrierLimit) "Must be positive or zero"
        { barrierLimit = barrierLimit
          barrierLeft = barrierLimit
          semaphore = Semaphore(0) }

    /// <summary>
    ///
    /// </summary>
    /// <returns> true if waiter is leader </returns>
    member this.Wait() : Future<bool> = future {
        let rec loop (this: Barrier) (expectedBarrierLeft: int) : Future<bool> =
            if expectedBarrierLeft > 1 then
                let newBarrierLeft = expectedBarrierLeft - 1
                let actualBarrierLeft =
                    Interlocked.CompareExchange(&this.barrierLeft, newBarrierLeft, expectedBarrierLeft)
                if expectedBarrierLeft <> actualBarrierLeft
                then loop this actualBarrierLeft
                else
                    future {
                        do! this.semaphore.Acquire()
                        return false
                    }
            elif expectedBarrierLeft = 1 then
                let newBarrierLeft = this.barrierLimit
                let actualBarrierLeft =
                    Interlocked.CompareExchange(&this.barrierLeft, newBarrierLeft, expectedBarrierLeft)
                if expectedBarrierLeft <> actualBarrierLeft
                then loop this actualBarrierLeft
                else
                    future {
                        do this.semaphore.Release(this.barrierLimit - 1)
                        return true
                    }
            elif expectedBarrierLeft = 0 then
                // Шайтан-калитка
                future { return true }
            else
                raise (UnreachableException())

        return! loop this this.barrierLeft
    }


[<RequireQualifiedAccess>]
module Barrier =
    let inline create (barrierLimit: int) : Barrier = Barrier(barrierLimit)
    let inline wait (barrier: Barrier) : Future<bool> = barrier.Wait()
