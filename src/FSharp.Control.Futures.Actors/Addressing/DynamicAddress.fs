namespace FSharp.Control.Futures.Actors.Addressing

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing


[<AbstractClass>]
type BaseDynamicAddress =
    abstract Send<'i, 'o>: message: 'i -> Future<'o>
    interface IDynamicAddress with
        member this.Send<'i, 'o>(message) = this.Send<'i, 'o>(message)
        member this.Narrow<'i, 'o>(): IAddress<'i, 'o> = Addresses.NarrowedAddress(this)

[<RequireQualifiedAccess>]
module DynamicAddress =
    let send<'i, 'o> (message: 'i) (address: IDynamicAddress) : Future<'o> =
        address.Send(message)

    let narrow<'i, 'o> (address: IDynamicAddress) : IAddress<'i, 'o> =
        address.Narrow()
