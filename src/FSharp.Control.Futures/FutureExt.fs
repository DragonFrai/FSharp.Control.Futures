
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
module Future =

    let catch (f: Future<'a>) : Future<Result<'a, exn>> =
        let mutable result = ValueNone
        Future.Core.create
        <| fun context ->
            if result.IsNone then
                try
                    Future.Core.poll context f |> Poll.onReady ^fun x -> result <- ValueSome (Ok x)
                with
                | e -> result <- ValueSome (Error e)
            match result with
            | ValueSome r -> Poll.Ready r
            | ValueNone -> Poll.Pending
        <| fun () -> f.Cancel()

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

    [<RequireQualifiedAccess>]
    module Seq =

        let iter (seq: 'a seq) (body: 'a -> unit) =
            Future.lazy' ^fun () -> for x in seq do body x

        let iterAsync (source: 'a seq) (body: 'a -> Future<unit>) =
            let enumerator = source.GetEnumerator()
            let mutable currentAwaited: Future<unit> voption = ValueNone
            let mutable isCancelled = false

            // Iterate enumerator until binded future return Ready () on poll
            // return ValueNone if enumeration was completed
            // else return ValueSome x, when x is Future<unit>
            let rec moveUntilReady (enumerator: IEnumerator<'a>) (binder: 'a -> Future<unit>) (context: Context) : Future<unit> voption =
                if enumerator.MoveNext()
                then
                    let waiter = body enumerator.Current
                    match Future.Core.poll context waiter with
                    | Poll.Ready () -> moveUntilReady enumerator binder context
                    | Poll.Pending -> ValueSome waiter
                else
                    ValueNone

            let rec pollInner (context: Context) : Poll<unit> =
                if isCancelled then raise FutureCancelledException
                match currentAwaited with
                | ValueNone ->
                    currentAwaited <- moveUntilReady enumerator body context
                    if currentAwaited.IsNone
                    then Poll.Ready ()
                    else Poll.Pending
                | ValueSome waiter ->
                    match waiter.Poll(context) with
                    | Poll.Ready () ->
                        currentAwaited <- ValueNone
                        pollInner context
                    | Poll.Pending -> Poll.Pending

            Future.Core.create pollInner (fun () -> isCancelled <- true)
