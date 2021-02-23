
// Includes an extension to the base methods of Future,
// which provides interoperability functionality that potentially
// requires interaction with the OS, such as timers, threads, etc.

[<AutoOpen>]
module FSharp.Control.Futures.SystemExt

open System
open System.Threading


[<RequireQualifiedAccess>]
module Future =

    let sleep (dueTime: TimeSpan) =

        let mutable timer: Timer = Unchecked.defaultof<_>
        // timer ready
        let mutable timeOut = false

        let inline onWake waker _ =
            let timer' = timer
            timer <- Unchecked.defaultof<_>
            timeOut <- true
            waker ()
            timer'.Dispose()

        let inline createTimer waker =
            new Timer(onWake waker, null, dueTime, Timeout.InfiniteTimeSpan)

        Future.Core.create ^fun waker ->
            if not (obj.ReferenceEquals(timer, null)) then invalidOp "polling Future.sleep before waking up "
            elif timeOut then Poll.Ready ()
            else
                timer <- createTimer waker
                Poll.Pending


    let sleepMs (milliseconds: int) =
        let dueTime = TimeSpan.FromMilliseconds((float) milliseconds)
        sleep dueTime


    /// The simplest implementation of the Future scheduler.
    /// Spawn a Future on current thread and synchronously waits for its Ready
    ///
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (f: Future<'a>) : 'a =
        // The simplest implementation of the Future scheduler.
        // Based on a polling cycle (polling -> waiting for awakening -> awakening -> polling)
        // until the point with the result is reached

        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let waker () = wh.Set() |> ignore

        let rec wait (current: Poll<'a>) =
            match current with
            | Poll.Ready x -> x
            | Poll.Pending ->
                wh.WaitOne() |> ignore
                wait (Future.Core.poll waker f)

        wait (Future.Core.poll waker f)

