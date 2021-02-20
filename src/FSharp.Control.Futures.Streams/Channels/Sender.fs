[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Streams.Channels.Sender

open System
open FSharp.Control.Futures


let send (msg: 'a) (sender: ISender<'a>) =
    sender.Send(msg)

// Create

let inline private closeCheck isClosed = if isClosed then raise (ObjectDisposedException "Sender already closed")

let onSend (action: 'a -> unit) =
    let mutable isClosed = false
    { new ISender<'a> with
        member _.Send(x) =
            closeCheck isClosed
            action x

        member _.Dispose() =
            if isClosed
            then raise (ObjectDisposedException "Double dispose")
            else ()
    }

let ignore<'a> =
    let mutable isClosed = false
    { new ISender<'a> with
        member _.Send(x) =
            closeCheck isClosed
            ignore x

        member _.Dispose() =
            if isClosed
            then raise (ObjectDisposedException "Double dispose")
            else ()
    }
