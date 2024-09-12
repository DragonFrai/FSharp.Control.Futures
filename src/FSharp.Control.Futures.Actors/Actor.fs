namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime



type ActorStatus =
    | Starting
    | Started
    | Stopping
    | Stopped

    with
        member this.IsAlive: bool =
            match this with
            | Starting | Started -> true
            | Stopping | Stopped -> false

        member this.IsDead: bool =
            match this with
            | Starting | Started -> false
            | Stopping | Stopped -> true

// [<RequireQualifiedAccess>]
// type StopMode =
//     | Soft
//     | Hard

type IActorContext =

    abstract Spawn: Future<'a> -> IFutureTask<'a>

    abstract SelfAddress: IActorAddress

    abstract Status: ActorStatus

    /// <summary>
    /// Stop receiving new messages and switch to Stopping status.
    /// All already queued messages will be handled ignored if actor not restored from stopping.
    /// </summary>
    abstract Stop: unit -> unit

    /// <summary>
    /// Stop receiving new messages and switch to Stopping status.
    /// All already queued messages will be handled.
    /// When all messages handled, switch to Stopped status.
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
