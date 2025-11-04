namespace FSharp.Control.Futures.Actors

open System
open System.Diagnostics
open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing



[<Class>]
type UnhandleableEnvelope =
    inherit Exception
    new () = { inherit Exception() }
    new (message: string) = { inherit Exception(message) }

[<Class>]
type UnhandleableEnvelopeMessage =
    inherit UnhandleableEnvelope
    new () = { inherit UnhandleableEnvelope() }
    new (message: string) = { inherit UnhandleableEnvelope(message) }

[<Class>]
type UnhandleableEnvelopeReply =
    inherit UnhandleableEnvelope
    new () = { inherit UnhandleableEnvelope() }
    new (message: string) = { inherit UnhandleableEnvelope(message) }

[<Class>]
type ActorStoppedException =
    inherit Exception
    new () = { inherit Exception() }
    new (message: string) = { inherit Exception(message) }
    new (message: string, innerException: exn) = { inherit Exception(message, innerException) }

[<RequireQualifiedAccess>]
type ActorStatus =
    | Created
    | Starting
    | Active
    | Stopping
    | Deleted

[<Interface>]
type IActorHandle =
    abstract Address: IDynamicAddress
    abstract Status: ActorStatus
    abstract Exception: exn option

[<Interface>]
type IActorContext =
    abstract Address: IDynamicAddress
    abstract Stop: unit -> unit
    abstract StopByException: exn -> unit

[<Struct>]
[<RequireQualifiedAccess>]
type ActorStoppingKind =
    | Request of isExternal: bool
    | Exception of exn: exn * isRaised: bool

[<Interface>]
type IActorStopping =
    abstract Kind: ActorStoppingKind
    abstract Cancel: unit -> unit

[<Interface>]
type IActor =
    abstract Start: context: IActorContext -> Future<unit>
    abstract Stop: context: IActorContext * stopping: IActorStopping -> Future<unit>
    abstract Receive: context: IActorContext * envelope: IEnvelope -> Future<unit>

[<AbstractClass>]
type BaseActor() =
    abstract Receive: context: IActorContext * envelope: IEnvelope -> Future<unit>
    interface IActor with
        member this.Receive(context, envelope) = this.Receive(context, envelope)
        member this.Start(_context) = Future.unit'
        member this.Stop(_context, _stopping) = Future.unit'
