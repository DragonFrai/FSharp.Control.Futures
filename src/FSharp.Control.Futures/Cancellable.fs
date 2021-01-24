module FSharp.Control.Futures.Cancellable


type MaybeCancel<'a> =
    | Completed of 'a
    | Cancelled

type CancellableFuture<'a> = IFuture<MaybeCancel<'a>>

exception FutureCancelledException of string

[<RequireQualifiedAccess>]
module Future =

    ()

