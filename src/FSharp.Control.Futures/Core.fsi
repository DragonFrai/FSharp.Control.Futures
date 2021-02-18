namespace FSharp.Control.Futures

[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit

type Waker = unit -> unit
