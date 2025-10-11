namespace FSharp.Control.Futures.Sync.Channels

open FSharp.Control.Futures


[<Interface>]
type IChannelSender<'a> =
    abstract Write: msg: 'a -> unit
    abstract TryWrite: msg: 'a -> Result<unit, unit>

[<Interface>]
type IChannelReceiver<'a> =
    abstract Receive: unit -> IFuture<'a>
    abstract TryReceive: unit -> 'a option

