[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Channel


let receive (receiver: IReceiver<'a>) =
    receiver.Receive()

let send (msg: 'a) (sender: ISender<'a>) =
    sender.Send(msg)
