namespace FSharp.Control.Futures.Streams.Channels

open System
open FSharp.Control.Futures.Streams


// Этот неймспейс предоставляет связку IStream и ISender,
// где последний представляет собой метод пересылки данных в IStream

// Core types

/// <summary> The point at which the message is sent to the stream.
/// It is an entry point, so sending is conditionally instant.  </summary>
/// <remarks> One instance can be used in only one thread, or maybe in many.
/// The interface does not yet provide a contract for this behavior. </remarks>
type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> unit

type IChannel<'a> =
    inherit ISender<'a>
    inherit IStream<'a>

// Publish types

type IPublishPollStream<'a> =
    inherit IStream<'a>
    abstract member Subscribe: unit -> IPublishPollStream<'a>

type IPublishChannel<'a> =
    inherit ISender<'a>
    inherit IPublishPollStream<'a>

