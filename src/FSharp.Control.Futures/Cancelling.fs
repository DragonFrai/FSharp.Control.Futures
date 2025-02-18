namespace FSharp.Control.Futures.Cancelling

open System.Threading
open FSharp.Control.Futures


[<Interface>]
type ICancelHandle =
    abstract Cancel: unit -> unit

[<Interface>]
type ICancellableFuture<'a> =
    inherit IFuture<'a>
    abstract CancelHandle: ICancelHandle

[<AutoOpen>]
module CancellationTokenExtensions =
    type CancellationToken with
        member this.RegisterFutureCancelHandle(cancelHandle: ICancelHandle): CancellationTokenRegistration =
            this.Register(fun () -> cancelHandle.Cancel())

// [<Class>]
// type CancellableFuture<'a>(future: Future<'a>) =
//
//     // 0 - init
//     // 1 - running
//     //
//
//     let mutable state = 0
//
//
//     abstract Poll: ctx: IContext -> Poll<'a>
//     abstract Drop: ctx
//     abstract Cancel: unit -> unit
//
//     interface
