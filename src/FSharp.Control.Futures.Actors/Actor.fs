namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Actors.Addressing




[<RequireQualifiedAccess>]
[<Struct>]
type SendError =
    | Terminated

type SendResult<'r> = Result<'r, SendError>

type ITryAddress<'m, 'r> = IAddress<'m, SendResult<'r>>

// HKT, GAT, anything, please...
[<Interface>]
type IDynamicTryAddress =
    abstract AsDynamicAddress: IDynamicAddress
    abstract Send<'m, 'r>: message: 'm -> Future<SendResult<'r>>
    abstract Narrow<'m, 'r> : unit -> ITryAddress<'m, 'r>






type IActorContext =

    abstract Spawn: Future<'a> -> IFutureTask<'a>

    abstract SelfAddress: IDynamicAddress

    /// <summary>
    /// Stop receiving new messages and switch to Stopping status.
    /// All already queued messages will be ignored if actor not restored from stopping.
    /// </summary>
    abstract Stop: unit -> unit

    /// <summary>
    /// Stop receiving new messages and switch to Stopping status.
    /// </summary>
    abstract Terminate: unit -> unit

[<Interface>]
type IActor =

    abstract Receive: ctx: IActorContext * msg: IEnvelope -> Future<unit>

    abstract Start: IActorContext -> unit

    /// <summary>
    /// Called when actor stopping.
    /// Cancel stopping, if `cancel` flag set to true.
    /// </summary>
    abstract OnStop: IActorContext * cancel: byref<bool> -> unit

    abstract Stop: IActorContext -> unit

[<AbstractClass>]
type BaseActor() =
    abstract Receive: ctx: IActorContext * dynMsg: IEnvelope -> Future<unit>
    interface IActor with
        member this.Receive(ctx, dynMsg) =



            this.Receive(ctx, dynMsg)
        member this.Start(_ctx) = ()
        member this.OnStop(_ctx, _cancel) = ()
        member this.Stop(_ctx) = ()
