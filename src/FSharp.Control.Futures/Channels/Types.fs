namespace FSharp.Control.Futures.Channels

open System
open System.Collections.Concurrent
open FSharp.Control.Futures


type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> Future<unit>

type IReceiver<'a> =
    // TODO: Change to 'unit -> Future<'a option>'
    abstract member Receive: unit -> Future<'a option>

type IChannel<'T> =
    inherit ISender<'T>
    inherit IReceiver<'T>
