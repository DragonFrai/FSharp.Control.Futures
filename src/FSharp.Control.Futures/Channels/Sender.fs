[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Sender


let send (msg: 'a) (sender: ISender<'a>) =
    sender.Send(msg)
