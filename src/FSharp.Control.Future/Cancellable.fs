module FSharp.Control.Future.Cancellable

open System.Threading

type MaybeCancel<'a> =
    | Completed of 'a
    | Cancelled

type CancellableFuture<'a> = Future<MaybeCancel<'a>>

exception FutureCancelledException of string

[<RequireQualifiedAccess>]
module Future =

    ()

