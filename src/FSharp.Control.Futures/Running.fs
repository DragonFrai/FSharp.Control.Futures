[<AutoOpen>]
module FSharp.Control.Futures.Running

module Future =

    open System.Threading
    open FSharp.Control.Futures.Core

    /// Spawn a Future on current thread and synchronously waits for its Ready
    /// The simplest implementation of the Future scheduler.
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (comp: Future<'a>) : 'a =
        // The simplest implementation of the Future scheduler.
        // Based on a polling cycle (polling -> waiting for awakening -> awakening -> polling -> ...)
        // until the point with the result is reached
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let mutable currentFut = comp
        let ctx =
            { new IContext with
                member _.Wake() = wh.Set() |> ignore
                member _.Scheduler = None
            }

        let rec pollWhilePending () =
            let rec pollTransiting () =
                match (Future.poll ctx currentFut) with
                | Poll.Ready x -> x
                | Poll.Pending ->
                    wh.WaitOne() |> ignore
                    pollWhilePending ()
                | Poll.Transit f ->
                    currentFut <- f
                    pollTransiting ()
            pollTransiting ()

        pollWhilePending ()
