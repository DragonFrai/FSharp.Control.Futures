[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Sender

open System
open FSharp.Control.Futures


let send (msg: 'a) (sender: ISender<'a>) =
    sender.Send(msg)

// Create

let onSend (action: 'a -> unit) =
    let mutable isClosed = false
    { new ISender<'a> with
        member _.Send(x) =
            if isClosed then raise (ObjectDisposedException "onSend sender")
            action x

        member _.Dispose() =
            isClosed <- true
    }

let ignore<'a> =
    let mutable isClosed = false
    { new ISender<'a> with
        member _.Send(x) =
            if isClosed then raise (ObjectDisposedException "ignore sender")
            ignore x

        member _.Dispose() =
            isClosed <- true
    }
