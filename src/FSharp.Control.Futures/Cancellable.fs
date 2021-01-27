[<AutoOpen>]
module FSharp.Control.Futures.Cancellable

open System


type MaybeCancel<'a> =
    | Completed of 'a
    | Cancelled of OperationCanceledException

type CancellableFuture<'a> = Future<MaybeCancel<'a>>

exception FutureCancelledException of OperationCanceledException

[<RequireQualifiedAccess>]
module Future =
    let getCancellable (fut: CancellableFuture<'a>) = future {
        let! cRes = fut
        return
            match cRes with
            | Completed x -> x
            | Cancelled ex -> raise ex
    }

