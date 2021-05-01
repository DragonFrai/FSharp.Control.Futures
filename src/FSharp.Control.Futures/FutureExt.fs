
// Includes an extension of the base Future methods,
// which are also intended for integration with the base types BCL and FSharp.Core.
// (Excluding things like system timers and potential OS interactions)

[<AutoOpen>]
module FSharp.Control.Futures.FutureExt

open System
open System.Collections.Generic
open System.Threading

exception FutureCancelledException


[<RequireQualifiedAccess>]
module AsyncComputation =

    let catch (source: IAsyncComputation<'a>) : IAsyncComputation<Result<'a, exn>> =
        let mutable _source = source
        let mutable _result = Poll.Pending
        AsyncComputation.create
        <| fun context ->
            if Poll.isPending _result then
                try
                    AsyncComputation.poll context _source |> Poll.onReady ^fun x -> _result <- Poll.Ready (Ok x)
                with
                | e -> _result <- Poll.Ready (Error e)
            _result
        <| fun () -> AsyncComputation.cancelNullable _source

    let sleep (dueTime: TimeSpan) =
        let mutable _timer: Timer = Unchecked.defaultof<_>
        let mutable _timeOut = false

        let inline onWake (context: Context) _ =
            let timer' = _timer
            _timer <- Unchecked.defaultof<_>
            _timeOut <- true
            context.Wake()
            timer'.Dispose()

        let inline createTimer context =
            new Timer(onWake context, null, dueTime, Timeout.InfiniteTimeSpan)

        AsyncComputation.create
        <| fun context ->
            if _timeOut then Poll.Ready ()
            else
                _timer <- createTimer context
                Poll.Pending
        <| fun () ->
            _timer.Dispose()
            do ()

    let sleepMs (milliseconds: int) =
        let dueTime = TimeSpan.FromMilliseconds(float milliseconds)
        sleep dueTime

    /// Spawn a Future on current thread and synchronously waits for its Ready
    /// The simplest implementation of the Future scheduler.
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (f: IAsyncComputation<'a>) : 'a =
        // The simplest implementation of the Future scheduler.
        // Based on a polling cycle (polling -> waiting for awakening -> awakening -> polling -> ...)
        // until the point with the result is reached
        use wh = new EventWaitHandle(false, EventResetMode.AutoReset)
        let ctx =
            { new Context() with member _.Wake() = wh.Set() |> ignore }

        let rec wait (current: Poll<'a>) =
            match current with
            | Poll.Ready x -> x
            | Poll.Pending ->
                wh.WaitOne() |> ignore
                wait (AsyncComputation.poll ctx f)

        wait (AsyncComputation.poll ctx f)

    [<RequireQualifiedAccess>]
    module Seq =

        /// <summary> Creates a future iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>iterAsync</code> and <code>yieldWorkflow</code> </remarks>
        let iter (seq: 'a seq) (body: 'a -> unit) =
            AsyncComputation.lazy' (fun () -> for x in seq do body x)

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let iterAsync (source: 'a seq) (body: 'a -> IAsyncComputation<unit>) =
            let enumerator = source.GetEnumerator()
            let mutable _currentAwaited: IAsyncComputation<unit> voption = ValueNone
            let mutable _isCancelled = false

            // Iterate enumerator until binded future return Ready () on poll
            // return ValueNone if enumeration was completed
            // else return ValueSome x, when x is Future<unit>
            let rec moveUntilReady (enumerator: IEnumerator<'a>) (binder: 'a -> IAsyncComputation<unit>) (context: Context) : IAsyncComputation<unit> voption =
                if enumerator.MoveNext()
                then
                    let waiter = body enumerator.Current
                    match AsyncComputation.poll context waiter with
                    | Poll.Ready () -> moveUntilReady enumerator binder context
                    | Poll.Pending -> ValueSome waiter
                else
                    ValueNone

            let rec pollInner (context: Context) : Poll<unit> =
                if _isCancelled then raise FutureCancelledException
                match _currentAwaited with
                | ValueNone ->
                    _currentAwaited <- moveUntilReady enumerator body context
                    if _currentAwaited.IsNone
                    then Poll.Ready ()
                    else Poll.Pending
                | ValueSome waiter ->
                    match waiter.Poll(context) with
                    | Poll.Ready () ->
                        _currentAwaited <- ValueNone
                        pollInner context
                    | Poll.Pending -> Poll.Pending

            AsyncComputation.create pollInner (fun () -> _isCancelled <- true)


module Future =

    let inline catch (source: Future<'a>) : Future<Result<'a, exn>> =
        Future.create (fun () -> AsyncComputation.catch (Future.run source))

    let inline sleep (dueTime: TimeSpan) =
        Future.create (fun () -> AsyncComputation.sleep dueTime)

    let inline sleepMs (milliseconds: int) =
        let dueTime = TimeSpan.FromMilliseconds(float milliseconds)
        sleep dueTime

    /// Spawn a Future on current thread and synchronously waits for its Ready
    /// The simplest implementation of the Future scheduler.
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (fut: Future<'a>) : 'a =
        // Here you can directly call the Raw representation of the Future,
        // since the current thread already represents the computation context
        AsyncComputation.runSync (Future.run fut)

    [<RequireQualifiedAccess>]
    module Seq =

        /// <summary> Creates a future iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>iterAsync</code> and <code>yieldWorkflow</code> </remarks>
        let inline iter (seq: 'a seq) (body: 'a -> unit) =
            Future.create (fun () -> AsyncComputation.Seq.iter seq body)

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let inline iterAsync (source: 'a seq) (body: 'a -> Future<unit>) =
            Future.create (fun () -> AsyncComputation.Seq.iterAsync source (body >> Future.run))


