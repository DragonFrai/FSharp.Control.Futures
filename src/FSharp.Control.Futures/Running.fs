[<AutoOpen>]
module FSharp.Control.Futures.Running

open FSharp.Control.Futures.Internals

module Future =

    open System.Threading
    open FSharp.Control.Futures

    /// Spawn a Future on current thread and synchronously waits for its Ready
    /// The simplest implementation of the Future scheduler.
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (fut: Future<'a>) : 'a =
        // The simplest implementation of the Future scheduler.
        // Based on a polling cycle (polling -> waiting for awakening -> awakening -> polling -> ...)
        // until the point with the result is reached
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let mutable fut = fut
        let ctx =
            { new IContext with member _.Wake() = wh.Set() |> ignore }

        let rec pollWhilePending (poller: NaivePoller<'a>) =
            let mutable poller = poller
            match poller.Poll(ctx) with
            | NaivePoll.Ready x -> x
            | NaivePoll.Pending ->
                wh.WaitOne() |> ignore
                pollWhilePending (poller)

        pollWhilePending (NaivePoller(fut))
