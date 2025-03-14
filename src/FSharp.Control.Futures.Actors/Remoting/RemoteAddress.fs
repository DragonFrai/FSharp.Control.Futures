namespace FSharp.Control.Futures.Actors.Remoting

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing


[<RequireQualifiedAccess>]
[<Struct>]
type RemoteSendError =
    | Terminated
    | Timeout

type RemoteSendResult = Result<unit, RemoteSendError>

type IRemoteAddress<'m> = IAddress<'m, RemoteSendResult>

[<Interface>]
type IDynamicRemoteAddress =
    abstract AsDynamicAddress: IDynamicAddress
    abstract Send<'m>: message: 'm -> Future<RemoteSendResult>
    abstract Narrow<'m> : unit -> IRemoteAddress<'m>
