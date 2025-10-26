namespace FSharp.Control.Futures.Actors

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Actors.Addressing



type UnhandleableMessage =
    inherit Exception
    new () = { inherit Exception() }
    new (message: string) = { inherit Exception(message) }

type UnsupportedMessageReply =
    inherit UnhandleableMessage
    new () = { inherit UnhandleableMessage() }
    new (message: string) = { inherit UnhandleableMessage(message) }

[<Interface>]
type IActor =

    abstract Receive: envelope: IEnvelope -> Future<unit>

