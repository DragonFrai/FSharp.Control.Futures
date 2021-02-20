namespace FSharp.Control.Futures.SeqStream.Channels

open System
open FSharp.Control.Futures.SeqStream


// Этот неймспейс предоставляет связку PollStream и ISender,
// где последний представляет собой метод пересылки данных в SeqStream

// Core types

/// One instance can be used in only one thread, or maybe in many. The interface does not yet provide a contract for this behavior.
type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> unit

type IChannel<'a> =
    inherit ISender<'a>
    inherit IPollStream<'a>

// Broadcast types

type IPublishPollStream<'a> =
    inherit IPollStream<'a>
    abstract member Subscribe: unit -> IPublishPollStream<'a>

type IPublishChannel<'a> =
    inherit ISender<'a>
    inherit IPublishPollStream<'a>

