[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Receiver


let receive (receiver: IReceiver<'a>) =
    receiver.Receive()
