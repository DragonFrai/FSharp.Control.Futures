namespace FSharp.Control.Futures.Channels

open System
open FSharp.Control.Futures


// Этот неймспейс предоставляет связку SeqStream и ISender,
// где последний представляет собой метод пересылки данных в SeqStream

// Core types

/// One instance can be used in only one thread, or maybe in many. The interface does not yet provide a contract for this behavior.
type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> unit

type IChannel<'a> =
    inherit ISender<'a>
    inherit ISeqStream<'a>

// Broadcast types

type IBroadcastSeqStream<'a> =
    inherit ISeqStream<'a>
    abstract member Subscribe: unit -> IBroadcastSeqStream<'a>

type IBroadcastChannel<'a> =
    inherit ISender<'a>
    inherit IBroadcastSeqStream<'a>

