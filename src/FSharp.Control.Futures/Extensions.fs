[<AutoOpen>]
module FSharp.Control.Futures.Extensions

open System
open System.Threading

open FSharp.Control.Futures
open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Internals


type FutureFuseException(message: string) = inherit Exception(message)
type FutureFuseReadyException() = inherit FutureFuseException("Future was polled after returning Ready.")
type FutureFuseCancelledException() = inherit FutureFuseException("Future was polled after being cancelled.")
type FutureFuseTransitedException() = inherit FutureFuseException("Future was polled after returning Transit.")

[<RequireQualifiedAccess>]
module Future =

    let catch (source: Future<'a>) : Future<Result<'a, exn>> =
        let mutable _source = source // TODO: Make separate class for remove FSharpRef in closure
        { new Future<_> with
            member _.Poll(ctx) =
                try
                    pollTransiting _source ctx
                    <| fun x ->
                        Poll.Ready (Ok x)
                    <| fun () -> Poll.Pending
                    <| fun f -> _source <- f
                with e ->
                    Poll.Ready (Error e)
            member _.Cancel() =
                cancelIfNotNull _source
        }

    type SleepFuture(duration: TimeSpan) =
        let mutable _timer: Timer = Unchecked.defaultof<_>
        let mutable _timeOut = false
        interface Future<unit> with
            member _.Poll(ctx) =
                let inline onWake (context: IContext) _ =
                    let timer' = _timer
                    _timer <- Unchecked.defaultof<_>
                    _timeOut <- true
                    context.Wake()
                    timer'.Dispose()
                let inline createTimer context =
                    new Timer(onWake context, null, duration, Timeout.InfiniteTimeSpan)

                if _timeOut then Poll.Ready ()
                else
                    _timer <- createTimer ctx
                    Poll.Pending

            member _.Cancel() =
                _timer.Dispose()

    //#region OS
    let sleep (duration: TimeSpan) : Future<unit> =
        upcast SleepFuture(duration)

    let sleepMs (millisecondDuration: int) =
        let duration = TimeSpan(days=0, hours=0, minutes=0, seconds=0, milliseconds=millisecondDuration)
        sleep duration

    type FuseFuture<'a>(fut: Future<'a>) =
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

    /// <summary>
    /// Creates a Future that will throw a specific <see cref="FutureFuseException">FutureFuseException</see> if polled after returning Ready or Transit or being cancelled.
    /// </summary>
    /// <exception cref="FutureFuseReadyException">Throws if Future was polled after returning Ready.</exception>
    /// <exception cref="FutureFuseTransitedException">Throws if Future was polled after returning Transit.</exception>
    /// <exception cref="FutureFuseCancelledException">Throws if Future was polled after being cancelled.</exception>
    let fuse (fut: Future<'a>) : Future<'a> =
        upcast FuseFuture<'a>(fut)

    /// <summary> Creates a Computation that ignore result of the passed Computation </summary>
    /// <returns> Computation that ignore result of the passed Computation </returns>
    let ignore (fut: Future<'a>) : Future<unit> =
        fut |> Future.map ignore
