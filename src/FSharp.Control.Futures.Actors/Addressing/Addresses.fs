namespace FSharp.Control.Futures.Actors.Addressing

open FSharp.Control.Futures


[<RequireQualifiedAccess>]
module internal Addresses =
    [<Class>]
    [<Sealed>]
    type NarrowedAddress<'i, 'o>(target: IDynamicAddress) =
        interface IAddress<'i, 'o> with
            member this.Send(message) = target.Send<'i, 'o>(message)

    [<Class>]
    [<Sealed>]
    type TransformAddress<'i, 'o, 'j, 'u>(address: IAddress<'i, 'o>, mapMsg: 'j -> 'i, mapRep: 'o -> 'u) =
        interface IAddress<'j, 'u> with
            member this.Send(message) = future {
                let message' = mapMsg message
                let! result' = address.Send(message')
                let result = mapRep result'
                return result
            }


