namespace FSharp.Control.Futures.Channels

open System
open FSharp.Control.Futures


// Core types

/// One instance can be used in only one thread, or maybe in many. The interface does not yet provide a contract for this behavior.
type ISender<'a> =
    inherit IDisposable
    abstract member Send: 'a -> unit

exception DoubleReceiveException of string

/// Return when channel is close
[<Struct>]
type ReceiveError =
    | Closed

type Result<'a> = Result<'a, ReceiveError>

/// Каждый экземпляр должен опрашиваться только в одной точке приема, в одном потоке, если реализацией не оговорено обратное.
/// Повторная попытка приёма без ожидания предыдущего значения считается ошибкой, если обратное не оговорено реализацией.
type IReceiver<'a> =
    abstract member Receive: unit -> Future<Result<'a, ReceiveError>>

type IChannel<'a> =
    inherit ISender<'a>
    inherit IReceiver<'a>

// Broadcast types

type IBroadcastReceiver<'a> =
    inherit IReceiver<'a>
    abstract member Subscribe: unit -> IBroadcastReceiver<'a>

type IBroadcastChannel<'a> =
    inherit ISender<'a>
    inherit IBroadcastReceiver<'a>

