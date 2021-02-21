[<AutoOpen>]
module FSharp.Control.Futures.FutureExt

open System.Collections.Generic


// Includes an extension of the base Future methods,
// which are also intended for integration with the base types BCL and FSharp.Core.
// (Excluding things like system timers and potential OS interactions)

[<RequireQualifiedAccess>]
module Future =

    let catch (f: Future<'a>) : Future<Result<'a, exn>> =
        let mutable result = ValueNone
        Future.Core.create ^fun waker ->
            if result.IsNone then
                try
                    Future.Core.poll waker f |> Poll.onReady ^fun x -> result <- ValueSome (Ok x)
                with
                | e -> result <- ValueSome (Error e)
            match result with
            | ValueSome r -> Ready r
            | ValueNone -> Pending

    // TODO: Make monadic Future OR Move to Seq
    let iter (seq: 'a seq) (action: 'a -> unit) =
        Future.lazy' ^fun () -> for x in seq do action x

    // TODO: Make monadic Future OR Move to Seq
    let iterFuture (source: 'a seq) (actionBind: 'a -> Future<unit>) =
        let enumerator = source.GetEnumerator()
        let mutable currentAwaited: Future<unit> voption = ValueNone

        // Iterate enumerator until binded future return Ready () on poll
        // return ValueNone if enumeration was completed
        // else return ValueSome x, when x is Future<unit>
        let rec moveUntilReady (enumerator: IEnumerator<'a>) (binder: 'a -> Future<unit>) (waker: Waker) : Future<unit> voption =
            if enumerator.MoveNext()
            then
                let waiter = actionBind enumerator.Current
                match Future.Core.poll waker waiter with
                | Ready () -> moveUntilReady enumerator binder waker
                | Pending -> ValueSome waiter
            else
                ValueNone

        let rec pollInner (waker: Waker) : Poll<unit> =
            match currentAwaited with
            | ValueNone ->
                currentAwaited <- moveUntilReady enumerator actionBind waker
                if currentAwaited.IsNone
                then Ready ()
                else Pending
            | ValueSome waiter ->
                match waiter.Poll(waker) with
                | Ready () ->
                    currentAwaited <- ValueNone
                    pollInner waker
                | Pending -> Pending

        Future.Core.create pollInner

    let yieldWorkflow () =
        Future.Core.create ^fun waker ->
            waker ()
            Pending
