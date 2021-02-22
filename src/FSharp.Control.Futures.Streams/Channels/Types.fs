namespace FSharp.Control.Futures.Streams.Channels

open System
open FSharp.Control.Futures.Streams


// Этот неймспейс предоставляет связку PollStream и ISender,
// где последний представляет собой метод пересылки данных в SeqStream

// Core types

/// One instance can be used in only one thread, or maybe in many. The interface does not yet provide a contract for this behavior.
type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> unit

type IChannel<'a> =
    inherit ISender<'a>
    inherit IPullStream<'a>

// Publish types

type IPublishPollStream<'a> =
    inherit IPullStream<'a>
    abstract member Subscribe: unit -> IPublishPollStream<'a>

type IPublishChannel<'a> =
    inherit ISender<'a>
    inherit IPublishPollStream<'a>

