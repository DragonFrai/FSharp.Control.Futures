[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Sender

open System
open FSharp.Control.Futures


let send (msg: 'a) (sender: ISender<'a>) =
    sender.Send(msg)

// Create
let onSent (action: 'a -> unit) =
    let mutable isDisposed = false
    { new ISender<'a> with
        member _.Send(x) =
            Future.lazy' ^fun () -> action x

        member _.Dispose() =
            if isDisposed
            then raise (ObjectDisposedException "Double dispose")
            else ()
    }

// Create
let ignore<'a> =
    let mutable isDisposed = false
    { new ISender<'a> with
        member _.Send(x) = Future.ready ()

        member _.Dispose() =
            if isDisposed
            then raise (ObjectDisposedException "Double dispose")
            else ()
    }

