namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures


[<Interface>]
type IActorAddress =

    /// TBD
    abstract SendMsg : msg: DynMsg -> Future<unit>

    /// TBD
    abstract Narrow<'i, 'o> : unit -> IAddress<'i, 'o>

[<Interface>]
type IActorAddress<'a> =
    inherit IActorAddress

[<RequireQualifiedAccess>]
module ActorAddress =
    let inline sendMsg (msg: DynMsg) (addr: IActorAddress) : Future<unit> =
        addr.SendMsg(msg)

// [ Base impl ]

[<Class>]
[<Sealed>]
type internal NarrowAddress<'i, 'o>(addr: IActorAddress) =
    inherit BaseAddress<'i, 'o>() with
    override this.Post(msg) =
        addr.SendMsg(msg)

[<AbstractClass>]
type BaseActorAddress() =
    abstract Post : DynMsg -> Future<unit>

    interface IActorAddress with
        member this.SendMsg(msg) =
            this.Post(msg)

        member this.Narrow() =
            NarrowAddress(this)
