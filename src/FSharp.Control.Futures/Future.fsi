namespace FSharp.Control.Futures

open System

// Contains the basic functions for creating and transforming `Future`.
// If the function accepts types other than `Future` or `Waker`, then they should be placed somewhere else

[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit

type Waker = unit -> unit

[<Interface>]
type IFuture<'a> =
    abstract member Poll : Waker -> Poll<'a>

type Future<'a> = IFuture<'a>

[<RequireQualifiedAccess>]
module Future =

    [<RequireQualifiedAccess>]
    module Core =

        val inline create: __expand_poll: (Waker -> Poll<'a>) -> Future<'a>

        val inline poll: waker: Waker -> fut: Future<'a> -> Poll<'a>

        val getWaker: Future<Waker>

        val unit: Future<unit>

        val never: Future<'a>


    val ready: value: 'a -> Future<'a>

    val unit: unit -> Future<unit>

    val lazy': f: (unit -> 'a) -> Future<'a>

    val never: unit -> Future<'a>

    val bind: binder: ('a -> Future<'b>) -> fut: Future<'a> -> Future<'b>

    val map: mapping: ('a -> 'b) -> fut: Future<'a> -> Future<'b>

    val apply: f: Future<'a -> 'b> -> fut: Future<'a> -> Future<'b>

    val merge: fut1: Future<'a> -> fut2: Future<'b> -> Future<'a * 'b>

    val join: fut: Future<Future<'a>> -> Future<'a>

    val delay: creator: (unit -> Future<'a>) -> Future<'a>

    val ignore: future: Future<'a> -> Future<unit>

