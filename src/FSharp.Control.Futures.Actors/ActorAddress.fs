namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures


type IActorAddress =

    /// TBD
    abstract Post : msg: DynMsg -> Future<unit>

    /// TBD
    abstract Narrow<'i, 'o> : unit -> IAddress<'i, 'o>

[<RequireQualifiedAccess>]
module ActorAddress =
    let inline post (msg: DynMsg) (addr: IActorAddress) : Future<unit> =
        addr.Post(msg)

// [ Base impl ]

[<Class>]
[<Sealed>]
type internal NarrowAddress<'i, 'o>(addr: IActorAddress) =
    inherit BaseAddress<'i, 'o>() with
    override this.Post(msg) =
        addr.Post(msg)

[<AbstractClass>]
type BaseActorAddress() =
    abstract Post : DynMsg -> Future<unit>

    interface IActorAddress with
        member this.Post(msg) =
            this.Post(msg)

        member this.Narrow() =
            NarrowAddress(this)
