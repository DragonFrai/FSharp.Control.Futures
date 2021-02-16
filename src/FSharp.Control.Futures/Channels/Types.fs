namespace FSharp.Control.Futures.Channels

open System
open FSharp.Control.Futures


// Core types

type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> Future<unit>

type IReceiver<'a> =
    abstract member Receive: unit -> Future<'a option>

type IChannel<'a> =
    inherit ISender<'a>
    inherit IReceiver<'a>

// Broadcast types

type IBroadcastReceiver<'a> =
    inherit IReceiver<'a>
    abstract member Broadcast: unit -> IBroadcastReceiver<'a>

type IBroadcastChannel<'a> =
    inherit ISender<'a>
    inherit IBroadcastReceiver<'a>

