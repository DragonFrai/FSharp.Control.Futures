namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime





// [<RequireQualifiedAccess>]
// type StopMode =
//     | Soft
//     | Hard

type IActorContext =

    abstract Spawn: Future<'a> -> IFutureTask<'a>

    abstract SelfAddress: IActorAddress

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

    abstract Receive: ctx: IActorContext * msg: DynMsg -> Future<unit>

    abstract Start: IActorContext -> unit

    /// <summary>
    /// Called when actor stopping.
    /// Cancel stopping, if `cancel` flag set to true.
    /// </summary>
    abstract OnStop: IActorContext * cancel: byref<bool> -> unit

    abstract Stop: IActorContext -> unit

[<AbstractClass>]
type BaseActor() =
    abstract Receive: ctx: IActorContext * dynMsg: DynMsg -> Future<unit>
    interface IActor with
        member this.Receive(ctx, dynMsg) = this.Receive(ctx, dynMsg)
        member this.Start(_ctx) = ()
        member this.OnStop(_ctx, _cancel) = ()
        member this.Stop(_ctx) = ()
