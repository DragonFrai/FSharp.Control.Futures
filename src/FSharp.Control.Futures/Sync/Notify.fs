namespace FSharp.Control.Futures.Sync

open System
open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync
open FSharp.Control.Futures.LowLevel
open Microsoft.FSharp.Core


/// <summary>
/// Примитив синхронизации который может заменить
/// `EventWaitHandle` в режиме работы `ResetEventMode.Auto` из стандартной библиотеки.
/// </summary>
type Notify =
    val mutable waitCounter: int // 1 - set, 0 - unset, < 0 - has waiters
    val semaphore: Semaphore

    new(notified: bool) =
        { waitCounter = if notified then 1 else 0
          semaphore = Semaphore(0) }

    new() =
        Notify(false)


    member this.Wait() : Future<unit> = future {
        let newWaitCounter = Interlocked.Add(&this.waitCounter, -1)
        Trace.Assert(newWaitCounter <= 0)
        if newWaitCounter = 0 then
            return ()
        else
            return! this.semaphore.Acquire()
    }

    member this.Notify() : unit =
        // return true if semaphore must release resources
        let rec incCounter (this: Notify) waitCounter =
            Trace.Assert(waitCounter <= 1)
            if waitCounter = 1 then false
            else
            let newWaitCounter = waitCounter + 1
            let prev = Interlocked.CompareExchange(&this.waitCounter, newWaitCounter, waitCounter)
            if prev <> waitCounter then incCounter this prev
            else
                waitCounter < 0

        let requireRelease = incCounter this this.waitCounter
        if requireRelease then
            this.semaphore.Release()

    member this.NotifyWaiters() : unit =
        // return count semaphore permits that must be released
        let rec resetWaiters (this: Notify) waitCounter =
            Trace.Assert(waitCounter <= 1)
            if waitCounter >= 0 then 0
            else
                let newWaitCounter = 0
                let prev = Interlocked.CompareExchange(&this.waitCounter, newWaitCounter, waitCounter)
                if prev <> waitCounter then resetWaiters this prev
                else -waitCounter

        let requireRelease = resetWaiters this this.waitCounter
        this.semaphore.Release(requireRelease)


[<RequireQualifiedAccess>]
module Notify =
    let inline create () = Notify()
    let inline notified () = Notify(true)
    let inline wait (notify: Notify) : Future<unit> = notify.Wait()
    let inline notify (notify: Notify) : unit = notify.Notify()
    let inline notifyWaiters (notify: Notify) : unit = notify.NotifyWaiters()
