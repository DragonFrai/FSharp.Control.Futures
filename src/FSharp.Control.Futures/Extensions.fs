namespace FSharp.Control.Futures

open System
open System.Threading

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


[<AutoOpen>]
module Extensions =

    module Futures =
        type ClrThreadPoolTimerSleep(duration: TimeSpan) =
            let mutable _timer: Timer = nullObj
            let mutable _notify: PrimaryNotify = PrimaryNotify(false, false)

            member internal this.OnWake() : unit =
                let _isSuccess = _notify.Notify()
                _timer <- nullObj

            interface Future<unit> with
                member this.Poll(ctx) =
                    let isNotified = _notify.Poll(ctx)
                    if isNotified then
                        Poll.Ready ()
                    else
                        if isNull _timer then
                            _timer <- new Timer((fun _ -> this.OnWake()), null, duration, Timeout.InfiniteTimeSpan)
                        Poll.Pending

                member _.Drop() =
                    let isNotified = _notify.Drop()
                    if isNotified then
                        if isNotNull _timer then
                            _timer.Dispose()
                            _timer <- nullObj

    [<RequireQualifiedAccess>]
    module Future =

        /// Spawn a Future on current thread and synchronously waits for its Ready
        /// The simplest implementation of the Future runtime.
        /// Equivalent to `(Runtime.spawn fut anyRuntime).Join()`,
        /// but without the cost of complex general purpose scheduler synchronization
        let runBlocking (fut: Future<'a>) : 'a =
            // The simplest implementation of the Future scheduler.
            // Based on a polling cycle (polling -> waiting for awakening -> awakening -> polling -> ...)
            // until the point with the result is reached
            use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
            let ctx =
                { new NotFeaturedContext() with
                    override _.Wake() = wh.Set() |> ignore }

            let rec pollWhilePending (poller: NaiveFuture<'a>) =
                let mutable poller = poller
                match poller.Poll(ctx) with
                | NaivePoll.Ready x -> x
                | NaivePoll.Pending ->
                    wh.WaitOne() |> ignore
                    pollWhilePending poller

            pollWhilePending (NaiveFuture(fut))

        let inline private timespanMs (millisecondDuration: int) : TimeSpan =
            TimeSpan(days=0, hours=0, minutes=0, seconds=0, milliseconds=millisecondDuration)

        let inline sleep (duration: TimeSpan) : Future<unit> =
            Futures.ClrThreadPoolTimerSleep(duration)

        let sleepMs (millisecondDuration: int) =
            sleep (timespanMs millisecondDuration)

        let timeout (duration: TimeSpan) (fut: Future<'a>) : Future<Result<'a, TimeoutException>> =
            Future.first
                (fut |> Future.map Ok)
                (sleep duration |> Future.map (fun _ -> Error (TimeoutException())))

        let timeoutMs (millisecondDuration: int) (fut: Future<'a>) : Future<Result<'a, TimeoutException>> =
            timeout (timespanMs millisecondDuration) fut
