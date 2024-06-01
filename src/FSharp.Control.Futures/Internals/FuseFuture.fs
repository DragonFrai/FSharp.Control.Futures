namespace FSharp.Control.Futures.Internals

open System
open FSharp.Control.Futures


// [Fuse future]
[<AutoOpen>]
module FuseFuture =

    type FutureFuseException(message: string) = inherit Exception(message)
    type FutureFuseReadyException() = inherit FutureFuseException("Future was polled after returning Ready.")
    type FutureFuseCancelledException() = inherit FutureFuseException("Future was polled after being cancelled.")
    type FutureFuseTransitedException() = inherit FutureFuseException("Future was polled after returning Transit.")

    [<RequireQualifiedAccess>]
    module Future =
        type internal Fuse<'a>(fut: Future<'a>) =
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
                member _.Drop() =
                    isCancelled <- true


        /// <summary>
        /// Creates a Future that will throw a specific <see cref="FutureFuseException">FutureFuseException</see> if polled after returning Ready or Transit or being cancelled.
        /// </summary>
        /// <exception cref="FutureFuseReadyException">Throws if Future was polled after returning Ready.</exception>
        /// <exception cref="FutureFuseTransitedException">Throws if Future was polled after returning Transit.</exception>
        /// <exception cref="FutureFuseCancelledException">Throws if Future was polled after being cancelled.</exception>
        let fuse (fut: Future<'a>) : Future<'a> =
            upcast Fuse<'a>(fut)
