namespace FSharp.Control.Futures.Actors.Addressing

open FSharp.Control.Futures


[<RequireQualifiedAccess>]
module Address =

    let send (message: 'i) (address: IAddress<'i, 'o>) : Future<'o> =
        address.Send(message)


