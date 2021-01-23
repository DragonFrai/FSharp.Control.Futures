namespace FSharp.Control.Futures

[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit

type Waker = unit -> unit

[<Struct>]
type Future<'a> = Future of (Waker -> Poll<'a>)

module Future =

    val poll: waker: Waker -> fut: Future<'a> -> Poll<'a>

    val ready: value: 'a -> Future<'a>

    val lazy': f: (unit -> 'a) -> Future<'a>

    val never: unit -> Future<'a>

    val bind: binder: ('a -> Future<'b>) -> fut: Future<'a> -> Future<'b>

    val map: mapping: ('a -> 'b) -> fut: Future<'a> -> Future<'b>

    val apply: f: Future<'a -> 'b> -> fut: Future<'a> -> Future<'b>

    val merge: fut1: Future<'a> -> fut2: Future<'b> -> Future<'a * 'b>

    val join: fut: Future<Future<'a>> -> Future<'a>

    val getWaker: Future<Waker>

    val ignore: future: Future<'a> -> Future<unit>

