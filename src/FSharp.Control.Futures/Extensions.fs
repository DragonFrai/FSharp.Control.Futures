[<AutoOpen>]
module FSharp.Control.Futures.Extensions

open System
open System.Threading

open FSharp.Control.Futures
open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Internals


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

    /// <summary> Creates a Computation that ignore result of the passed Computation </summary>
    /// <returns> Computation that ignore result of the passed Computation </returns>
    let ignore (fut: Future<'a>) : Future<unit> =
        fut |> Future.map ignore
