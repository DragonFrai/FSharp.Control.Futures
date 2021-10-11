[<AutoOpen>]
module FSharp.Control.Futures.Extensions

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
module Future =

    open System.Threading

    [<RequireQualifiedAccess>]
    module Seq =

        /// <summary> Creates a future iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>iterAsync</code> and <code>yieldWorkflow</code> </remarks>
        let iter (seq: 'a seq) (body: 'a -> unit) =
            Future.lazy' (fun () -> for x in seq do body x)

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let iterAsync (source: 'a seq) (body: 'a -> Future<unit>) =
            let rec iterAsyncEnumerator body (enumerator: IEnumerator<'a>) =
                if enumerator.MoveNext() then
                    Future.bind (fun () -> iterAsyncEnumerator body enumerator) (body enumerator.Current)
                else
                    Future.readyUnit
            Future.delay (fun () -> iterAsyncEnumerator body (source.GetEnumerator()))

    //#endregion

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
