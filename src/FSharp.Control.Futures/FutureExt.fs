
// Includes an extension of the base Future methods,
// which are also intended for integration with the base types BCL and FSharp.Core.
// (Excluding things like system timers and potential OS interactions)

[<AutoOpen>]
module FSharp.Control.Futures.FutureExt

open System.Collections.Generic


[<RequireQualifiedAccess>]
module Future =

    let catch (f: Future<'a>) : Future<Result<'a, exn>> =
        let mutable result = ValueNone
        Future.Core.create ^fun context ->
            if result.IsNone then
                try
                    Future.Core.poll context f |> Poll.onReady ^fun x -> result <- ValueSome (Ok x)
                with
                | e -> result <- ValueSome (Error e)
            match result with
            | ValueSome r -> Poll.Ready r
            | ValueNone -> Poll.Pending

    let yieldWorkflow () =
        let mutable isYielded = false
        Future.Core.create ^fun context ->
            if isYielded then
                Poll.Ready ()
            else
                isYielded <- true
                context.Wake()
                Poll.Pending


    [<RequireQualifiedAccess>]
    module Seq =

        let iter (seq: 'a seq) (body: 'a -> unit) =
            Future.lazy' ^fun () -> for x in seq do body x

        let iterAsync (source: 'a seq) (body: 'a -> Future<unit>) =
            let enumerator = source.GetEnumerator()
            let mutable currentAwaited: Future<unit> voption = ValueNone

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

            Future.Core.create pollInner
