[<AutoOpen>]
module FSharp.Control.Futures.Extensions

open System
open System.Threading

open FSharp.Control.Futures
open FSharp.Control.Futures.Types
open FSharp.Control.Futures.Internals


type FutureFuseException(message: string) = inherit Exception(message)
type FutureFuseReadyException() = inherit FutureFuseException("Future was polled after returning Ready.")
type FutureFuseCancelledException() = inherit FutureFuseException("Future was polled after being cancelled.")
type FutureFuseTransitedException() = inherit FutureFuseException("Future was polled after returning Transit.")


[<RequireQualifiedAccess>]
module Future =

    type Sleep(duration: TimeSpan) =
        let mutable _timer: Timer = nullObj
        let mutable _notify: PrimaryNotify = PrimaryNotify(false)

        member internal this.OnWake() : unit =
            let _isCancelled = _notify.Notify()
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

            member _.Cancel() =
                let isNotified = _notify.Cancel()
                if isNotified then
                    if isNotNull _timer then
                        _timer <- nullObj

    type Fuse<'a>(fut: Future<'a>) =
        let mutable isReady = false
        let mutable isCancelled = false
        let mutable isTransited = false
        interface Future<'a> with
            member _.Poll(ctx) =
                if isReady then raise (FutureFuseReadyException())
                elif isCancelled then raise (FutureFuseCancelledException())
                elif isTransited then raise (FutureFuseTransitedException())
                else
                    let p = Future.poll ctx fut
                    match p with
                    | Poll.Pending -> Poll.Pending
                    | Poll.Ready x ->
                        isReady <- true
                        Poll.Ready x
                    | Poll.Transit f ->
                        isTransited <- true
                        Poll.Transit f
            member _.Cancel() =
                isCancelled <- true


    //#region OS
    let sleep (duration: TimeSpan) : Future<unit> =
        upcast Sleep(duration)

    let sleepMs (millisecondDuration: int) =
        let duration = TimeSpan(days=0, hours=0, minutes=0, seconds=0, milliseconds=millisecondDuration)
        sleep duration

    /// <summary>
    /// Creates a Future that will throw a specific <see cref="FutureFuseException">FutureFuseException</see> if polled after returning Ready or Transit or being cancelled.
    /// </summary>
    /// <exception cref="FutureFuseReadyException">Throws if Future was polled after returning Ready.</exception>
    /// <exception cref="FutureFuseTransitedException">Throws if Future was polled after returning Transit.</exception>
    /// <exception cref="FutureFuseCancelledException">Throws if Future was polled after being cancelled.</exception>
    let fuse (fut: Future<'a>) : Future<'a> =
        upcast Fuse<'a>(fut)

