namespace FSharp.Control.Futures

open System


[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit

type Waker = unit -> unit

[<AbstractClass>]
type Future<'a> =
    new: unit -> Future<'a>
    abstract member Poll : Waker -> Poll<'a>


module Future =

    module Core =

        [<Obsolete("Inherit class from FSharp.Control.Futures.Future")>]
        val inline create: f: (Waker -> Poll<'a>) -> Future<'a>

        val inline poll: waker: Waker -> fut: Future<'a> -> Poll<'a>

    val ready: value: 'a -> Future<'a>

    val lazy': f: (unit -> 'a) -> Future<'a>

    val never: unit -> Future<'a>

    val bind: binder: ('a -> Future<'b>) -> fut: Future<'a> -> Future<'b>

    val map: mapping: ('a -> 'b) -> fut: Future<'a> -> Future<'b>

    val apply: f: Future<'a -> 'b> -> fut: Future<'a> -> Future<'b>

    val merge: fut1: Future<'a> -> fut2: Future<'b> -> Future<'a * 'b>

    val join: fut: Future<Future<'a>> -> Future<'a>

    val delay: creator: (unit -> Future<'a>) -> Future<'a>

    val getLastWaker: Future<Waker>

    val ignore: future: Future<'a> -> Future<unit>

