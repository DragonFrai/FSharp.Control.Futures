
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

        let inline onWake (context: Context) _ =
            let timer' = timer
            timer <- Unchecked.defaultof<_>
            timeOut <- true
            context.Wake()
            timer'.Dispose()

        let inline createTimer context =
            new Timer(onWake context, null, dueTime, Timeout.InfiniteTimeSpan)

        Future.Core.create
        <| fun context ->
            if timeOut then Poll.Ready ()
            else
                timer <- createTimer context
                Poll.Pending
        <| fun () ->
            timer.Dispose()
            // TODO: impl
            do ()

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
        let ctx =
            { new Context() with member _.Wake() = wh.Set() |> ignore }


        let rec wait (current: Poll<'a>) =
            match current with
            | Poll.Ready x -> x
            | Poll.Pending ->
                wh.WaitOne() |> ignore
                wait (Future.Core.poll ctx f)

        wait (Future.Core.poll ctx f)

